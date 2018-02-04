using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Common.Log;
using JetBrains.Annotations;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Mappers;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Services.Events;
using MarginTrading.Backend.Services.Notifications;
using MarginTrading.Contract.RabbitMqMessageModels;
using MarginTrading.Backend.Services.TradingConditions;

namespace MarginTrading.Backend.Services
{
    public class AccountManager: TimerPeriod 
    {
        private readonly AccountsCacheService _accountsCacheService;
        private readonly IMarginTradingAccountsRepository _repository;
        private readonly IConsole _console;
        private readonly MarginSettings _marginSettings;
        private readonly IRabbitMqNotifyService _rabbitMqNotifyService;
        private readonly IAccountGroupCacheService _accountGroupCacheService;
        private readonly IMarginTradingAccountsRepository _accountsRepository;
        private readonly ITradingConditionsCacheService _tradingConditionsCacheService;
        private readonly ILog _log;
        private readonly IClientNotifyService _clientNotifyService;
        private readonly OrdersCache _ordersCache;
        private readonly IEventChannel<AccountBalanceChangedEventArgs> _acountBalanceChangedEventChannel;
        private readonly ITradingEngine _tradingEngine;

        private static readonly ConcurrentDictionary<int, SemaphoreSlim> Semaphores = new ConcurrentDictionary<int, SemaphoreSlim>();
        
        public AccountManager(AccountsCacheService accountsCacheService,
            IMarginTradingAccountsRepository repository,
            IConsole console,
            MarginSettings marginSettings,
            IRabbitMqNotifyService rabbitMqNotifyService,
            IAccountGroupCacheService accountGroupCacheService,
            IMarginTradingAccountsRepository accountsRepository,
            ITradingConditionsCacheService tradingConditionsCacheService,
            ILog log,
            IClientNotifyService clientNotifyService,
            OrdersCache ordersCache,
            IEventChannel<AccountBalanceChangedEventArgs> acountBalanceChangedEventChannel,
            ITradingEngine tradingEngine)
            : base(nameof(AccountManager), 60000, log)
        {
            _accountsCacheService = accountsCacheService;
            _repository = repository;
            _console = console;
            _marginSettings = marginSettings;
            _rabbitMqNotifyService = rabbitMqNotifyService;
            _accountGroupCacheService = accountGroupCacheService;
            _accountsRepository = accountsRepository;
            _tradingConditionsCacheService = tradingConditionsCacheService;
            _log = log;
            _clientNotifyService = clientNotifyService;
            _ordersCache = ordersCache;
            _acountBalanceChangedEventChannel = acountBalanceChangedEventChannel;
            _tradingEngine = tradingEngine;
        }

        
        #region TimePeriod
        
        public override Task Execute()
        {
            var accounts = GetAccountsToWriteStats();
            var accountsStatsMessages = GenerateAccountsStatsUpdateMessages(accounts);
            var tasks = accountsStatsMessages.Select(m => _rabbitMqNotifyService.UpdateAccountStats(m));

            return Task.WhenAll(tasks);
        }
        
        public override void Start()
        {
            var accounts = _repository.GetAllAsync().GetAwaiter().GetResult()
                .Select(MarginTradingAccount.Create).GroupBy(x => x.ClientId).ToDictionary(x => x.Key, x => x.ToArray());

            _accountsCacheService.InitAccountsCache(accounts);
            _console.WriteLine($"InitAccountsCache (clients count:{accounts.Count})");

            base.Start();
        }

        private IReadOnlyList<IMarginTradingAccount> GetAccountsToWriteStats()
        {
            var accountsIdsToWrite = _ordersCache.GetActive().Select(a => a.AccountId).Distinct().ToHashSet();
            return _accountsCacheService.GetAll().Where(a => accountsIdsToWrite.Contains(a.Id)).ToList();
        }

        private IEnumerable<AccountStatsUpdateMessage> GenerateAccountsStatsUpdateMessages(IReadOnlyList<IMarginTradingAccount> accounts)
        {
            var accountStats = accounts.Select(a => a.ToRabbitMqContract(_marginSettings.IsLive));

            var chunks = accountStats.ToChunks(100);

            foreach (var chunk in chunks)
            {
                yield return new AccountStatsUpdateMessage {Accounts = chunk.ToArray()};
            }
        }
        
        #endregion
       

        public async Task<string> UpdateBalanceAsync(IMarginTradingAccount account, decimal amount, AccountHistoryType historyType, string comment, string eventSourceId = null, bool changeTransferLimit = false)
        {
            if (historyType == AccountHistoryType.Deposit && changeTransferLimit)
            {
                CheckDepositLimits(account, amount);
            }

            if (changeTransferLimit)
            {
                CheckTransferLimits(account, amount);
            }

            var semaphore = GetSemaphore(account);

            await semaphore.WaitAsync();

            try
            {
                var updatedAccount =
                    await _repository.UpdateBalanceAsync(account.ClientId, account.Id, amount, changeTransferLimit);
                _acountBalanceChangedEventChannel.SendEvent(this, new AccountBalanceChangedEventArgs(updatedAccount));
                //todo: move to separate event consumers
                _accountsCacheService.UpdateBalance(updatedAccount);
                _clientNotifyService.NotifyAccountUpdated(updatedAccount);

                var transactionId = Guid.NewGuid().ToString("N");
                
                await _rabbitMqNotifyService.AccountHistory(
                    transactionId,
                    account.Id,
                    account.ClientId,
                    amount,
                    updatedAccount.Balance,
                    updatedAccount.WithdrawTransferLimit,
                    historyType,
                    comment,
                    eventSourceId);

                return transactionId;
            }
            finally
            {
                semaphore.Release();
            }
        }

        public async Task DeleteAccountAsync(string clientId, string accountId)
        {
            var orders = _ordersCache.GetAll().Where(o => o.AccountId == accountId).ToArray();
            
            if (orders.Any())
            {
                throw new Exception(
                    $"Account [{accountId}] has not closed orders: [{orders.Select(o => $"{o.Id}:{o.Status.ToString()}").ToJson()}]");
            }
            
            var account = _accountsCacheService.Get(clientId, accountId);
            await _repository.DeleteAsync(clientId, accountId);
            await ProcessAccountsSetChange(clientId);
            await _rabbitMqNotifyService.AccountDeleted(account);
        }

        //TODO: close/remove all orders
        public Task<string> ResetAccountAsync(string clientId, string accountId)
        {
            var account = _accountsCacheService.Get(clientId, accountId);

            return UpdateBalanceAsync(account, LykkeConstants.DefaultDemoBalance - account.Balance,
                AccountHistoryType.Reset,
                "Reset account");
        }

        public async Task AddAccountAsync(string clientId, string baseAssetId, string tradingConditionId)
        {
            var accountGroup =
                _accountGroupCacheService.GetAccountGroup(tradingConditionId, baseAssetId);

            if (accountGroup == null)
            {
                throw new Exception(
                    $"Account group with base asset [{baseAssetId}] and trading condition [{tradingConditionId}] is not found");
            }

            var clientAccounts = _accountsCacheService.GetAll(clientId);

            if (clientAccounts.Any(a => a.BaseAssetId == baseAssetId && a.TradingConditionId == tradingConditionId))
            {
                throw new Exception(
                    $"Client [{clientId}] already has account with base asset [{baseAssetId}] and trading condition [{tradingConditionId}]");
            }

            var account = CreateAccount(clientId, baseAssetId, tradingConditionId);
            await _repository.AddAsync(account);
            await ProcessAccountsSetChange(account.ClientId);
            await _rabbitMqNotifyService.AccountCreated(account);
        }

        public async Task<MarginTradingAccount[]> CreateDefaultAccounts(string clientId, string tradingConditionsId = null)
        {
            var existingAccounts = (await _accountsRepository.GetAllAsync(clientId)).ToList();

            if (existingAccounts.Any())
            {
                var accounts = existingAccounts.Select(MarginTradingAccount.Create).ToArray();
                _accountsCacheService.UpdateAccountsCache(clientId, accounts);
                return accounts;
            }

            if (string.IsNullOrEmpty(tradingConditionsId))
                tradingConditionsId = GetTradingConditions();

            var baseAssets = GetBaseAssets(tradingConditionsId);

            var newAccounts = new List<MarginTradingAccount>();

            foreach (var baseAsset in baseAssets)
            {
                try
                {
                    var account = CreateAccount(clientId, baseAsset, tradingConditionsId);
                    await _repository.AddAsync(account);
                    await _rabbitMqNotifyService.AccountCreated(account);
                    newAccounts.Add(account);
                }
                catch (Exception e)
                {
                    await _log.WriteErrorAsync(nameof(AccountManager), "Create default accounts",
                        $"clientId={clientId}, tradingConditionsId={tradingConditionsId}", e);
                }
            }

            await ProcessAccountsSetChange(clientId, newAccounts);

            return newAccounts.ToArray();
        }

        public async Task<IReadOnlyList<IMarginTradingAccount>> CreateAccounts(string tradingConditionId,
            string baseAssetId)
        {
            var result = new List<IMarginTradingAccount>();
            
            var clientAccountGroups = _accountsCacheService.GetAll()
                .GroupBy(a => a.ClientId)
                .Where(g =>
                    g.Any(a => a.TradingConditionId == tradingConditionId)
                    && g.All(a => a.BaseAssetId != baseAssetId));

            foreach (var group in clientAccountGroups)
            {
                try
                {
                    var account = CreateAccount(group.Key, baseAssetId, tradingConditionId);
                    await _repository.AddAsync(account);
                    await _rabbitMqNotifyService.AccountCreated(account);
                    await ProcessAccountsSetChange(group.Key, group.Concat(new[] {account}).ToArray());
                    result.Add(account);
                }
                catch (Exception e)
                {
                    await _log.WriteErrorAsync(nameof(AccountManager), "Create accounts by account group",
                        $"clientId={group.Key}, tradingConditionsId={tradingConditionId}, baseAssetId={baseAssetId}",
                        e);
                }
            }

            return result;
        }
        
        public async Task<List<IOrder>> CloseAccountOrders(string accountId, OrderCloseReason reason)
        {
            var openedOrders = _ordersCache.ActiveOrders.GetOrdersByAccountIds(accountId).ToArray();
            var closedOrders = new List<IOrder>();

            foreach (var order in openedOrders)
            {
                try
                {
                    var closedOrder = await _tradingEngine.CloseActiveOrderAsync(order.Id, reason);
                    closedOrders.Add(closedOrder);
                }
                catch (Exception e)
                {
                    await _log.WriteWarningAsync(nameof(AccountManager), "CloseAccountActiveOrders",
                        $"AccountId: {accountId}, OrderId: {order.Id}", $"Error closing order: {e.Message}");
                }
            }

            return closedOrders;
        }

        public async Task<IMarginTradingAccount> SetTradingCondition(string clientId, string accountId,
            string tradingConditionId)
        {
            var result =
                await _accountsRepository.UpdateTradingConditionIdAsync(clientId, accountId, tradingConditionId);

            if (result != null)
            {
                _accountsCacheService.SetTradingCondition(clientId, accountId, tradingConditionId);

                await _clientNotifyService.NotifyTradingConditionsChanged(tradingConditionId, accountId);
            }
            
            return result;
        }

        
        #region Helpers

        private string[] GetBaseAssets(string tradingConditionsId)
        {
            var accountGroups =
                _accountGroupCacheService.GetAllAccountGroups().Where(g => g.TradingConditionId == tradingConditionsId);
            var baseAssets = accountGroups.Select(g => g.BaseAssetId).Distinct().ToArray();

            if (!baseAssets.Any())
                throw new Exception(
                    $"No account groups found for trading conditions {tradingConditionsId}");

            return baseAssets;
        }

        private string GetTradingConditions()
        {
            //use default trading conditions for demo
            if (!_marginSettings.IsLive)
            {
                var tradingConditions = _tradingConditionsCacheService.GetAllTradingConditions();
                var defaultConditions = tradingConditions.FirstOrDefault(item => item.IsDefault);

                if (defaultConditions == null)
                    throw new Exception("No default trading conditions set for demo");
                else
                    return defaultConditions.Id;
            }
            else
            {
                throw new Exception("No trading conditions found");
            }
        }

        [Pure]
        private MarginTradingAccount CreateAccount(string clientId, string baseAssetId, string tradingConditionId)
        {
            var id = $"{(_marginSettings.IsLive ? string.Empty : _marginSettings.DemoAccountIdPrefix)}{Guid.NewGuid():N}";
            var initialBalance = _marginSettings.IsLive ? 0 : LykkeConstants.DefaultDemoBalance;

            return new MarginTradingAccount
            {
                Id = id,
                BaseAssetId = baseAssetId,
                ClientId = clientId,
                Balance = initialBalance,
                TradingConditionId = tradingConditionId
            };
        }
        
        private void CheckDepositLimits(IMarginTradingAccount account, decimal amount)
        {
            //limit can not be more then max after deposit
            if (amount > 0)
            {
                var accountGroup =
                    _accountGroupCacheService.GetAccountGroup(account.TradingConditionId, account.BaseAssetId);

                if (accountGroup.DepositTransferLimit > 0 && accountGroup.DepositTransferLimit < account.Balance + amount)
                {
                    throw new Exception(
                        $"Margin Trading is in beta testing. The cash-ins are temporarily limited when Total Capital exceeds {accountGroup.DepositTransferLimit} {accountGroup.BaseAssetId}. Thank you for using Lykke Margin Trading, the limit will be cancelled soon!");
                }
            }
        }
        
        private void CheckTransferLimits(IMarginTradingAccount account, decimal amount)
        {
            //withdraw can not be more then limit
            if (amount < 0 && account.WithdrawTransferLimit < Math.Abs(amount))
            {
                throw new Exception(
                    $"Can not transfer {Math.Abs(amount)}. Current limit is {account.WithdrawTransferLimit}");
            }
        }
        
        private async Task ProcessAccountsSetChange(string clientId, IReadOnlyList<MarginTradingAccount> allClientsAccounts = null)
        {
            if (allClientsAccounts == null)
            {
                allClientsAccounts = (await _repository.GetAllAsync(clientId)).Select(MarginTradingAccount.Create).ToList();
            }

            _accountsCacheService.UpdateAccountsCache(clientId, allClientsAccounts);
            await _rabbitMqNotifyService.UserUpdates(false, true, new[] {clientId});
        }

        private SemaphoreSlim GetSemaphore(IMarginTradingAccount account)
        {
            var hash = account.Id.GetHashCode() % 100;

            return Semaphores.GetOrAdd(hash, new SemaphoreSlim(1, 1));
        }

        #endregion
        
    }
}