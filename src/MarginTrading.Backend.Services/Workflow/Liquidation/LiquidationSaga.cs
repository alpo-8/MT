using System;
using System.Linq;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using Lykke.Common.Chaos;
using Lykke.Cqrs;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Extensions;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Core.Repositories;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Services.AssetPairs;
using MarginTrading.Backend.Services.Workflow.Liquidation.Commands;
using MarginTrading.Backend.Services.Workflow.Liquidation.Events;
using MarginTrading.Backend.Services.Workflow.SpecialLiquidation.Commands;
using MarginTrading.Common.Services;

namespace MarginTrading.Backend.Services.Workflow.Liquidation
{
    public class LiquidationSaga
    {
        private readonly IDateService _dateService;
        private readonly IChaosKitty _chaosKitty;
        private readonly IOperationExecutionInfoRepository _operationExecutionInfoRepository;
        private readonly OrdersCache _ordersCache;
        private readonly CqrsContextNamesSettings _cqrsContextNamesSettings;
        private readonly ILog _log;
        private readonly IAccountsCacheService _accountsCacheService;
        private readonly IAssetPairDayOffService _assetPairDayOffService;

        public const string OperationName = "Liquidation";

        public LiquidationSaga(
            IDateService dateService,
            IChaosKitty chaosKitty,
            IOperationExecutionInfoRepository operationExecutionInfoRepository,
            OrdersCache ordersCache,
            CqrsContextNamesSettings cqrsContextNamesSettings,
            ILog log,
            IAccountsCacheService accountsCacheService,
            IAssetPairDayOffService assetPairDayOffService)
        {
            _dateService = dateService;
            _chaosKitty = chaosKitty;
            _operationExecutionInfoRepository = operationExecutionInfoRepository;
            _ordersCache = ordersCache;
            _cqrsContextNamesSettings = cqrsContextNamesSettings;
            _log = log;
            _accountsCacheService = accountsCacheService;
            _assetPairDayOffService = assetPairDayOffService;
        }
        
        [UsedImplicitly]
        public async Task Handle(LiquidationStartedInternalEvent e, ICommandSender sender)
        {
            var executionInfo = await _operationExecutionInfoRepository.GetAsync<LiquidationOperationData>(
                operationName: OperationName,
                id: e.OperationId);

            if (executionInfo?.Data == null)
            {
                return;
            }

            if (executionInfo.Data.SwitchState(LiquidationOperationState.Initiated, 
                LiquidationOperationState.Started))
            {

                LiquidatePositionsIfAnyAvailable(e.OperationId, executionInfo.Data, sender);

                _chaosKitty.Meow(
                    $"{nameof(LiquidationStartedInternalEvent)}:" +
                    $"Save_OperationExecutionInfo:" +
                    $"{e.OperationId}");

                await _operationExecutionInfoRepository.Save(executionInfo);
            }
        }

        [UsedImplicitly]
        public async Task Handle(LiquidationFailedInternalEvent e, ICommandSender sender)
        {
            var executionInfo = await _operationExecutionInfoRepository.GetAsync<LiquidationOperationData>(
                operationName: OperationName,
                id: e.OperationId);

            if (executionInfo?.Data == null)
            {
                return;
            }

            if (executionInfo.Data.State == LiquidationOperationState.Finished)
            {
                await _log.WriteWarningAsync(nameof(LiquidationSaga), nameof(LiquidationFailedInternalEvent),
                    e.ToJson(), $"Unable to set Failed state. Liquidation {e.OperationId} is already finished");
                return;
            }

            if (executionInfo.Data.SwitchState(executionInfo.Data.State, LiquidationOperationState.Failed))
            {   
                await _operationExecutionInfoRepository.Save(executionInfo);
            }
        }
        
        [UsedImplicitly]
        public async Task Handle(LiquidationFinishedInternalEvent e, ICommandSender sender)
        {
            var executionInfo = await _operationExecutionInfoRepository.GetAsync<LiquidationOperationData>(
                operationName: OperationName,
                id: e.OperationId);

            if (executionInfo?.Data == null)
            {
                return;
            }

            if (executionInfo.Data.SwitchState(LiquidationOperationState.Started, 
                LiquidationOperationState.Finished))
            {
                await _operationExecutionInfoRepository.Save(executionInfo);
            }
        }
        
        [UsedImplicitly]
        public async Task Handle(PositionsLiquidationFinishedInternalEvent e, ICommandSender sender)
        {
            var executionInfo = await _operationExecutionInfoRepository.GetAsync<LiquidationOperationData>(
                operationName: OperationName,
                id: e.OperationId);

            if (executionInfo?.Data == null)
            {
                return;
            }

            if (executionInfo.Data.SwitchState(LiquidationOperationState.Started, 
                LiquidationOperationState.Started))
            {
                executionInfo.Data.ProcessedPositionIds.AddRange(e.LiquidationInfos.Select(i => i.PositionId));
                executionInfo.Data.LiquidatedPositionIds.AddRange(e.LiquidationInfos.Where(i => i.IsLiquidated)
                    .Select(i => i.PositionId));
                
                ContinueOrFinishLiquidation(e.OperationId, executionInfo.Data, sender);
                
                _chaosKitty.Meow(
                    $"{nameof(PositionsLiquidationFinishedInternalEvent)}:" +
                    $"Save_OperationExecutionInfo:" +
                    $"{e.OperationId}");
                
                await _operationExecutionInfoRepository.Save(executionInfo);
            }
        }
        
        [UsedImplicitly]
        public async Task Handle(NotEnoughLiquidityInternalEvent e, ICommandSender sender)
        {
            var executionInfo = await _operationExecutionInfoRepository.GetAsync<LiquidationOperationData>(
                operationName: OperationName,
                id: e.OperationId);

            if (executionInfo?.Data == null)
            {
                return;
            }
           
            if (executionInfo.Data.SwitchState(LiquidationOperationState.Started, 
                LiquidationOperationState.SpecialLiquidationStarted))
            {
                sender.SendCommand(new StartSpecialLiquidationInternalCommand
                {
                    OperationId = Guid.NewGuid().ToString(),
                    CreationTime = _dateService.Now(),
                    AccountId = executionInfo.Data.AccountId,
                    PositionIds = e.PositionIds,
                    CausationOperationId = e.OperationId
                }, _cqrsContextNamesSettings.TradingEngine);
                
                _chaosKitty.Meow(
                    $"{nameof(PositionsLiquidationFinishedInternalEvent)}:" +
                    $"Save_OperationExecutionInfo:" +
                    $"{e.OperationId}");
                
                await _operationExecutionInfoRepository.Save(executionInfo);
            }
        }
        
        [UsedImplicitly]
        public async Task Handle(LiquidationResumedInternalEvent e, ICommandSender sender)
        {
            var executionInfo = await _operationExecutionInfoRepository.GetAsync<LiquidationOperationData>(
                operationName: OperationName,
                id: e.OperationId);

            if (executionInfo?.Data == null)
            {
                return;
            }

            var validState = e.IsCausedBySpecialLiquidation
                ? LiquidationOperationState.SpecialLiquidationStarted
                : executionInfo.Data.State;
            
            if (executionInfo.Data.SwitchState(validState, LiquidationOperationState.Started))
            {
                //if we are trying to resume liquidation, let's clean up processed positions to retry
                if (!e.IsCausedBySpecialLiquidation)
                {
                    executionInfo.Data.ProcessedPositionIds = executionInfo.Data.LiquidatedPositionIds;
                }
                
                ContinueOrFinishLiquidation(e.OperationId, executionInfo.Data, sender);
                
                _chaosKitty.Meow(
                    $"{nameof(PositionsLiquidationFinishedInternalEvent)}:" +
                    $"Save_OperationExecutionInfo:" +
                    $"{e.OperationId}");
                
                await _operationExecutionInfoRepository.Save(executionInfo);
            }
        }
        
        #region Private methods

        private (string AssetPairId, PositionDirection? Direction, string[] Positions)? GetLiquidationData(LiquidationOperationData data)
        {
            var positionsOnAccount = _ordersCache.Positions.GetPositionsByAccountIds(data.AccountId);

            //group positions and take only not processed, filtered and with open market
            var positionGroups = positionsOnAccount
                .Where(p => !data.ProcessedPositionIds.Contains(p.Id) && 
                            (string.IsNullOrEmpty(data.AssetPairId) || p.AssetPairId == data.AssetPairId) &&
                            (data.Direction == null || p.Direction == data.Direction))    
                .GroupBy(p => (p.AssetPairId, p.Direction))
                .Where(gr => !_assetPairDayOffService.IsDayOff(gr.Key.AssetPairId))
                .ToArray();

            IGrouping<(string AssetPairId, PositionDirection Direction), Position> targetPositions = null;

            if (data.LiquidationType == LiquidationType.Mco && data.Direction.HasValue)
            {
                var groupsWithZeroInitialMargin = positionGroups.Where(gr => gr.Sum(p => p.GetMcoInitialMargin()) == 0)
                    .Select(gr => gr.Key).ToHashSet();

                if (groupsWithZeroInitialMargin.Any())
                {
                    _log.WriteWarningAsync(nameof(LiquidationSaga), nameof(GetLiquidationData),
                        groupsWithZeroInitialMargin.ToJson(), $"Position groups with 0 initial margin were found");
                }
                
                //order groups by MCO level
                var orderedGroups = positionGroups
                    .Where(gr => !groupsWithZeroInitialMargin.Contains(gr.Key))
                    .OrderBy(gr => gr.Sum(p => p.GetMcoCurrentMargin()) / gr.Sum(p => p.GetMcoInitialMargin()));

                //get worst group depending on direction
                targetPositions = data.Direction == PositionDirection.Long
                    ? orderedGroups.FirstOrDefault()
                    : orderedGroups.LastOrDefault();

                return (targetPositions?.Key.AssetPairId, targetPositions?.Key.Direction, 
                    targetPositions?.Select(p => p.Id).ToArray() ?? new string[0]);
            }
            else if (data.LiquidationType == LiquidationType.Forced)
            {
                return (data.AssetPairId, data.Direction, 
                    positionGroups.SelectMany(x => x.Select(p => p.Id)).ToArray());
            }
            else
            {
                //take positions from group with max margin used
                targetPositions = positionGroups.OrderByDescending(gr => gr.Sum(p => p.GetMarginMaintenance())).FirstOrDefault();
                
                return (targetPositions?.Key.AssetPairId, targetPositions?.Key.Direction, 
                    targetPositions?.Select(p => p.Id).ToArray() ?? new string[0]);
            }
        }

        private void LiquidatePositionsIfAnyAvailable(string operationId,
            LiquidationOperationData data, ICommandSender sender)
        {
            var liquidationData = GetLiquidationData(data);

            if (!liquidationData.HasValue || !liquidationData.Value.Positions.Any())
            {
                sender.SendCommand(new FailLiquidationInternalCommand
                {
                    OperationId = operationId,
                    CreationTime = _dateService.Now(),
                    Reason = "Nothing to liquidate",
                    LiquidationType = data.LiquidationType,
                }, _cqrsContextNamesSettings.TradingEngine);
            }
            else
            {
                sender.SendCommand(new LiquidatePositionsInternalCommand
                {
                    OperationId = operationId,
                    CreationTime = _dateService.Now(),
                    PositionIds = liquidationData.Value.Positions,
                    AssetPairId = liquidationData.Value.AssetPairId,
                    Direction = liquidationData.Value.Direction,
                }, _cqrsContextNamesSettings.TradingEngine);
            }
        }
        
        private void ContinueOrFinishLiquidation(string operationId, LiquidationOperationData data, ICommandSender sender)
        {
            void FinishWithReason(string reason) => sender.SendCommand(new FinishLiquidationInternalCommand
                {
                    OperationId = operationId, 
                    CreationTime = _dateService.Now(), 
                    Reason = reason,
                    LiquidationType = data.LiquidationType
                }, _cqrsContextNamesSettings.TradingEngine);
            
            var account = _accountsCacheService.TryGet(data.AccountId);
            
            if (account == null)
            {
                sender.SendCommand(new FailLiquidationInternalCommand
                {
                    OperationId = operationId,
                    CreationTime = _dateService.Now(),
                    Reason = "Account does not exist",
                    LiquidationType = data.LiquidationType,
                }, _cqrsContextNamesSettings.TradingEngine);
                return;
            }
            
            var accountLevel = account.GetAccountLevel();

            if (data.LiquidationType == LiquidationType.Forced)
            {
                FinishWithReason("All positions are closed");

                return;
            }

            if (accountLevel < AccountLevel.StopOut
                || (data.LiquidationType == LiquidationType.Mco
                    && data.ProcessedPositionIds.All(p => data.LiquidatedPositionIds.Contains(p))))
            {
                FinishWithReason($"Account margin level is {accountLevel}");
            }
            else
            {
                LiquidatePositionsIfAnyAvailable(operationId, data, sender);
            }
        }
        
        #endregion
        
    }
}