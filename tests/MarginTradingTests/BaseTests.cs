﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Lykke.Service.ClientAccount.Client;
using Lykke.Service.ClientAccount.Client.AutorestClient.Models;
using Lykke.Service.ClientAccount.Client.Models;
using Lykke.Service.ExchangeConnector.Client;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.DayOffSettings;
using MarginTrading.Backend.Core.Orderbooks;
using MarginTrading.Backend.Core.Repositories;
using MarginTrading.Backend.Core.Services;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Services;
using MarginTrading.Backend.Services.AssetPairs;
using MarginTrading.Backend.Services.Caches;
using MarginTrading.Backend.Services.Events;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Backend.Services.Modules;
using MarginTrading.Backend.Services.Services;
using MarginTrading.Backend.Services.TradingConditions;
using MarginTrading.Common.RabbitMq;
using MarginTrading.Common.Services;
using MarginTrading.Common.Services.Settings;
using MarginTrading.SettingsService.Contracts;
using MarginTrading.SettingsService.Contracts.Scheduling;
using MarginTradingTests.Modules;
using Moq;

namespace MarginTradingTests
{
    public class BaseTests
    {
        private const string ClientId1 = "1";
        private const string ClientId2 = "2";

        protected IContainer Container { get; set; }

        protected void RegisterDependencies(bool mockEvents = false)
        {
            try
            {
                RegisterDependenciesCore(mockEvents);
            }
            catch (Exception e)
            {
                Debugger.Break();
                Console.WriteLine(e);
                throw;
            }
        }
        
        private void RegisterDependenciesCore(bool mockEvents = false)
        {
            var builder = new ContainerBuilder();

            var overnightMarginSettings = new OvernightMarginSettings();
            var marginSettings = new MarginTradingSettings
            {
                RabbitMqQueues =
                    new RabbitMqQueues
                    {
                        MarginTradingEnabledChanged = new RabbitMqQueueInfo {ExchangeName = ""}
                    },
                BlobPersistence = new BlobPersistenceSettings()
                {
                    FxRatesDumpPeriodMilliseconds = 10000,
                    QuotesDumpPeriodMilliseconds = 10000,
                    OrderbooksDumpPeriodMilliseconds = 5000,
                    OrdersDumpPeriodMilliseconds = 5000
                },
                ReportingEquivalentPricesSettings = new[]
                    {new ReportingEquivalentPricesSettings {EquivalentAsset = "USD", LegalEntity = "LYKKETEST"}},
                OvernightMargin = overnightMarginSettings,
            };

            builder.RegisterInstance(marginSettings).SingleInstance();
            builder.RegisterInstance(overnightMarginSettings).SingleInstance();
            builder.RegisterInstance(new RiskInformingSettings
            {
                Data = new[]
                {
                    new RiskInformingParams
                    {
                        EventTypeCode = "BE01",
                        Level = "None",
                        System = "QuotesMonitor",
                    },
                    new RiskInformingParams
                    {
                        EventTypeCode = "BE02",
                        Level = "None",
                        System = "QuotesMonitor",
                    }
                }
            }).SingleInstance();

            builder.RegisterModule(new MockBaseServicesModule());
            builder.RegisterModule(new MockRepositoriesModule());
            builder.RegisterModule(new MockExternalServicesModule(Accounts));
            
            if (mockEvents)
            {
                builder.RegisterModule(new MockEventModule());
            }
            else
            {
                builder.RegisterModule(new EventModule());
            }

            builder.RegisterModule(new CacheModule());
            builder.RegisterModule(new ServicesModule());
            builder.RegisterModule(new ManagersModule());
            
            builder.RegisterType<EventChannel<AccountBalanceChangedEventArgs>>()
                .As<IEventChannel<AccountBalanceChangedEventArgs>>()
                .SingleInstance();

            var settingsServiceMock = new Mock<IMarginTradingSettingsCacheService>();
            settingsServiceMock.Setup(s => s.IsMarginTradingEnabled(It.IsAny<string>()))
                .ReturnsAsync(new EnabledMarginTradingTypes {Live = true, Demo = true});
            settingsServiceMock.Setup(s => s.IsMarginTradingEnabled(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(true);

            builder.RegisterInstance(settingsServiceMock.Object)
                .As<IMarginTradingSettingsCacheService>()
                .SingleInstance();

            var clientAccountClientMock = new Mock<IClientAccountClient>();
            clientAccountClientMock.Setup(s => s.CreateWalletAsync(It.IsAny<string>(), It.IsAny<WalletType>(),
                    It.IsAny<OwnerType>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns((string clientId, WalletType walletType, OwnerType owner,
                    string name, string description) => Task.FromResult(
                    new WalletDtoModel
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = name,
                        Type = walletType.ToString(),
                        Description = description,
                        ClientId = clientId,
                    }));
            clientAccountClientMock.Setup(s => s.GetByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(() =>
                    new ClientModel {Email = "example@example.com", NotificationsId = Guid.NewGuid().ToString()});
            clientAccountClientMock.Setup(s => s.GetPushNotificationAsync(It.IsAny<string>()))
                .ReturnsAsync(() => new PushNotificationsSettingsModel {Enabled = true});
            
            builder.RegisterInstance(clientAccountClientMock.Object)
                .As<IClientAccountClient>()
                .SingleInstance();

            builder.RegisterInstance(new Mock<IOperationsLogService>().Object)
                .As<IOperationsLogService>()
                .SingleInstance();
            
            builder.RegisterType<ConvertService>().As<IConvertService>().SingleInstance();

            var scheduleSettingsApiMock = new Mock<IScheduleSettingsApi>();
            scheduleSettingsApiMock.Setup(m => m.StateList(It.IsAny<string[]>()))
                .ReturnsAsync(new List<CompiledScheduleContract>());
            builder.RegisterInstance(scheduleSettingsApiMock.Object).As<IScheduleSettingsApi>();

            var exchangeConnector = Mock.Of<IExchangeConnectorService>();
            builder.RegisterInstance(exchangeConnector).As<IExchangeConnectorService>();

            builder.RegisterBuildCallback(c => c.Resolve<AccountManager>());
            builder.RegisterBuildCallback(c => c.Resolve<TradingInstrumentsManager>());
            builder.RegisterBuildCallback(c => c.Resolve<OrderCacheManager>());
            builder.RegisterInstance(new Mock<IMtSlackNotificationsSender>(MockBehavior.Loose).Object).SingleInstance();
            builder.RegisterInstance(Mock.Of<IRabbitMqService>()).As<IRabbitMqService>();

            builder.RegisterType<SimpleIdentityGenerator>().As<IIdentityGenerator>();
            Container = builder.Build();

            MtServiceLocator.FplService = Container.Resolve<IFplService>();
            MtServiceLocator.AccountUpdateService = Container.Resolve<IAccountUpdateService>();
            MtServiceLocator.AccountsCacheService = Container.Resolve<IAccountsCacheService>();
            MtServiceLocator.SwapCommissionService = Container.Resolve<ICommissionService>();

            Container.Resolve<OrderBookList>().Init(null);
        }

        protected List<MarginTradingAccount> Accounts = new List<MarginTradingAccount>
        {
            new MarginTradingAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                TradingConditionId = "1",
                BaseAssetId = "USD",
                ClientId = ClientId1,
                Balance = 1000, 
                LegalEntity = "LYKKETEST"
            },
            new MarginTradingAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                TradingConditionId = "1",
                BaseAssetId = "EUR",
                ClientId = ClientId1,
                Balance = 1000, 
                LegalEntity = "LYKKETEST"
            },
            new MarginTradingAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                TradingConditionId = "1",
                BaseAssetId = "CHF",
                ClientId = ClientId1,
                Balance = 1000, 
                LegalEntity = "LYKKETEST"
            },

            new MarginTradingAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                TradingConditionId = "1",
                BaseAssetId = "USD",
                ClientId = ClientId2,
                Balance = 1000, 
                LegalEntity = "LYKKETEST"
            },
            new MarginTradingAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                TradingConditionId = "1",
                BaseAssetId = "EUR",
                ClientId = ClientId2,
                Balance = 1000, 
                LegalEntity = "LYKKETEST"
            },
            new MarginTradingAccount
            {
                Id = Guid.NewGuid().ToString("N"),
                TradingConditionId = "1",
                BaseAssetId = "CHF",
                ClientId = ClientId2,
                Balance = 1000, 
                LegalEntity = "LYKKETEST"
            }
        };
    }
}