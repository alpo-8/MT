﻿using AzureStorage.Tables;
using Common.Log;
using MarginTrading.AzureRepositories.Logs;

namespace MarginTrading.AzureRepositories
{
    public class AzureRepoFactories
    {
        public static class MarginTrading
        {
            public static TradingConditionsRepository CreateTradingConditionsRepository(string connstring, ILog log)
            {
                return new TradingConditionsRepository(AzureTableStorage<TradingConditionEntity>.Create(() => connstring,
                    "MarginTradingConditions", log));
            }

            public static AccountGroupRepository CreateAccountGroupRepository(string connstring, ILog log)
            {
                return new AccountGroupRepository(AzureTableStorage<AccountGroupEntity>.Create(() => connstring,
                    "MarginTradingAccountGroups", log));
            }

            public static AccountAssetsPairsRepository CreateAccountAssetsRepository(string connstring, ILog log)
            {
                return new AccountAssetsPairsRepository(AzureTableStorage<AccountAssetPairEntity>.Create(() => connstring,
                    "MarginTradingAccountAssets", log));
            }

            public static AssetPairsRepository CreateAssetsRepository(string connstring, ILog log)
            {
                return new AssetPairsRepository(AzureTableStorage<AssetPairEntity>.Create(() => connstring,
                    "MarginTradingAssets", log));
            }

            public static MarginTradingOrdersHistoryRepository CreateOrdersHistoryRepository(string connstring, ILog log)
            {
                return new MarginTradingOrdersHistoryRepository(AzureTableStorage<MarginTradingOrderHistoryEntity>.Create(() => connstring,
                    "MarginTradingOrdersHistory", log));
            }

            public static MarginTradingOrdersRejectedRepository CreateOrdersRejectedRepository(string connstring, ILog log)
            {
                return new MarginTradingOrdersRejectedRepository(AzureTableStorage<MarginTradingOrderRejectedEntity>.Create(() => connstring,
                    "MarginTradingOrdersRejected", log));
            }

            public static MarginTradingAccountHistoryRepository CreateAccountHistoryRepository(string connstring, ILog log)
            {
                return new MarginTradingAccountHistoryRepository(AzureTableStorage<MarginTradingAccountHistoryEntity>.Create(() => connstring,
                    "AccountsHistory", log));
            }

            public static MarginTradingAccountsRepository CreateAccountsRepository(string connstring, ILog log)
            {
                return new MarginTradingAccountsRepository(AzureTableStorage<MarginTradingAccountEntity>.Create(() => connstring,
                    "MarginTradingAccounts", log));
            }

            public static MarginTradingAccountStatsRepository CreateAccountStatsRepository(string connstring, ILog log)
            {
                return new MarginTradingAccountStatsRepository(AzureTableStorage<MarginTradingAccountStatsEntity>.Create(() => connstring,
                    "MarginTradingAccountStats", log));
            }

            public static MarginTradingBlobRepository CreateBlobRepository(string connstring)
            {
                return new MarginTradingBlobRepository(connstring);
            }

            public static MatchingEngineRoutesRepository CreateMatchingEngineRoutesRepository(string connstring, ILog log)
            {
                return new MatchingEngineRoutesRepository(AzureTableStorage<MatchingEngineRouteEntity>.Create(() => connstring,
                    "MatchingEngineRoutes", log));
            }

            public static RiskSystemCommandsLogRepository CreateRiskSystemCommandsLogRepository(string connstring, ILog log)
            {
                return new RiskSystemCommandsLogRepository(AzureTableStorage<RiskSystemCommandsLogEntity>.Create(() => connstring,
                    "RiskSystemCommandsLog", log));
            }
        }
    }
}