﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MarginTrading.Backend.Contracts.Account;
using MarginTrading.Backend.Contracts.Common;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Mappers;
using MarginTrading.Backend.Core.Orders;
using MarginTrading.Backend.Services;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Backend.Services.TradingConditions;
using MarginTrading.Backend.Services.Workflow.Liquidation.Commands;
using MarginTrading.Common.Extensions;
using MarginTrading.Common.Middleware;
using MarginTrading.Common.Services;
using MarginTrading.Contract.BackendContracts;
using MarginTrading.Contract.BackendContracts.AccountsManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IAccountsApi = MarginTrading.Backend.Contracts.IAccountsApi;

namespace MarginTrading.Backend.Controllers
{
    [Authorize]
    [Route("api/accounts")]
    [MiddlewareFilter(typeof(RequestLoggingPipeline))]
    public class AccountsController : Controller, IAccountsApi
    {
        private readonly IAccountsCacheService _accountsCacheService;
        private readonly IDateService _dateService;
        private readonly AccountManager _accountManager;
        private readonly IOrderReader _orderReader;
        private readonly TradingConditionsCacheService _tradingConditionsCache;
        private readonly ICqrsSender _cqrsSender;

        public AccountsController(IAccountsCacheService accountsCacheService,
            IDateService dateService,
            AccountManager accountManager,
            IOrderReader orderReader,
            TradingConditionsCacheService tradingConditionsCache,
            ICqrsSender cqrsSender)
        {
            _accountsCacheService = accountsCacheService;
            _dateService = dateService;
            _accountManager = accountManager;
            _orderReader = orderReader;
            _tradingConditionsCache = tradingConditionsCache;
            _cqrsSender = cqrsSender;
        }

        /// <summary>
        /// Returns all account stats
        /// </summary>
        [HttpGet]
        [Route("stats")]
        public Task<List<AccountStatContract>> GetAllAccountStats()
        {
            var stats = _accountsCacheService.GetAll();

            return Task.FromResult(stats.Select(Convert).ToList());
        }

        /// <summary>
        /// Returns all accounts stats, optionally paginated. Both skip and take must be set or unset.
        /// </summary>
        [HttpGet]
        [Route("stats/by-pages")]
        public Task<PaginatedResponseContract<AccountStatContract>> GetAllAccountStatsByPages(
            int? skip = null, int? take = null)
        {
            if ((skip.HasValue && !take.HasValue) || (!skip.HasValue && take.HasValue))
            {
                throw new ArgumentOutOfRangeException(nameof(skip), "Both skip and take must be set or unset");
            }

            if (take.HasValue && (take <= 0 || skip < 0))
            {
                throw new ArgumentOutOfRangeException(nameof(skip), "Skip must be >= 0, take must be > 0");
            }
            
            var stats = _accountsCacheService.GetAllByPages(skip, take);

            return Task.FromResult(Convert(stats));
        }

        /// <summary>
        /// Get accounts depending on active/open orders and positions for particular assets.
        /// </summary>
        /// <returns>List of account ids</returns>
        [HttpPost("/api/accounts")]
        public Task<List<string>> GetAllAccountIdsFiltered([FromBody] ActiveAccountsRequest request)
        {
            if (request.IsAndClauseApplied != null 
                && (request.ActiveOrderAssetPairIds == null || request.ActivePositionAssetPairIds == null))
            {
                throw new ArgumentException("isAndClauseApplied might be set only if both filters are set", 
                    nameof(request.IsAndClauseApplied));
            }

            List<string> accountIdsByOrders = null;
            if (request.ActiveOrderAssetPairIds != null)
            {
                accountIdsByOrders = _orderReader.GetPending()
                    .Where(x => request.ActiveOrderAssetPairIds.Count == 0
                                || request.ActiveOrderAssetPairIds.Contains(x.AssetPairId))
                    .Select(x => x.AccountId)
                    .Distinct()
                    .ToList();
            }

            List<string> accountIdsByPositions = null;
            if (request.ActivePositionAssetPairIds != null)
            {
                accountIdsByPositions = _orderReader.GetPositions()
                    .Where(x => request.ActivePositionAssetPairIds.Count == 0
                                || request.ActivePositionAssetPairIds.Contains(x.AssetPairId))
                    .Select(x => x.AccountId)
                    .Distinct()
                    .ToList();
            }

            if (accountIdsByOrders == null && accountIdsByPositions != null)
            {
                return Task.FromResult(accountIdsByPositions.OrderBy(x => x).ToList());
            }

            if (accountIdsByOrders != null && accountIdsByPositions == null)
            {
                return Task.FromResult(accountIdsByOrders.OrderBy(x => x).ToList());
            }

            if (accountIdsByOrders == null && accountIdsByPositions == null)
            {
                return Task.FromResult(_accountsCacheService.GetAll().Select(x => x.Id).OrderBy(x => x).ToList());
            }

            if (request.IsAndClauseApplied ?? false)
            {
                return Task.FromResult(accountIdsByOrders.Intersect(accountIdsByPositions)
                    .OrderBy(x => x).ToList());
            }

            return Task.FromResult(accountIdsByOrders.Concat(accountIdsByPositions)
                .Distinct().OrderBy(x => x).ToList());
        }

        /// <summary>
        /// Returns stats of selected account
        /// </summary>
        /// <param name="accountId"></param>
        [HttpGet]
        [Route("stats/{accountId}")]
        public Task<AccountStatContract> GetAccountStats(string accountId)
        {
            var stats = _accountsCacheService.Get(accountId);

            return Task.FromResult(Convert(stats));
        }
        
        [HttpPost, Route("resume-liquidation/{accountId}")]
        public Task ResumeLiquidation(string accountId, string comment)
        {
            var account = _accountsCacheService.Get(accountId);

            var liquidation = account.LiquidationOperationId;
            
            if (string.IsNullOrEmpty(liquidation))
            {
                throw new InvalidOperationException("Account is not in liquidation state");
            }
            
            _cqrsSender.SendCommandToSelf(new ResumeLiquidationInternalCommand
            {
                OperationId = liquidation,
                CreationTime = DateTime.UtcNow,
                IsCausedBySpecialLiquidation = false,
                Comment = comment
            });
            
            return Task.CompletedTask;
        }
        
        private static AccountStatContract Convert(IMarginTradingAccount item)
        {
            return new AccountStatContract
            {
                AccountId = item.Id,
                BaseAssetId = item.BaseAssetId,
                Balance = item.Balance,
                MarginCallLevel = item.GetMarginCall1Level(),
                StopOutLevel = item.GetStopOutLevel(),
                TotalCapital = item.GetTotalCapital(),
                FreeMargin = item.GetFreeMargin(),
                MarginAvailable = item.GetMarginAvailable(),
                UsedMargin = item.GetUsedMargin(),
                CurrentlyUsedMargin = item.GetCurrentlyUsedMargin(),
                InitiallyUsedMargin = item.GetInitiallyUsedMargin(),
                MarginInit = item.GetMarginInit(),
                PnL = item.GetPnl(),
                UnrealizedDailyPnl = item.GetUnrealizedDailyPnl(),
                OpenPositionsCount = item.GetOpenPositionsCount(),
                ActiveOrdersCount = item.GetActiveOrdersCount(),
                MarginUsageLevel = item.GetMarginUsageLevel(),
                LegalEntity = item.LegalEntity,
                IsInLiquidation = item.IsInLiquidation(),
                MarginNotificationLevel = item.GetAccountLevel().ToString()
            };
        }

        private PaginatedResponseContract<AccountStatContract> Convert(PaginatedResponse<MarginTradingAccount> accounts)
        {
            return new PaginatedResponseContract<AccountStatContract>(
                contents: accounts.Contents.Select(Convert).ToList(),
                start: accounts.Start,
                size: accounts.Size,
                totalSize: accounts.TotalSize
            );
        }
    }
}