﻿using System;
using Autofac;
using Common.Log;
using Lykke.HttpClientGenerator;
using Lykke.HttpClientGenerator.Retries;
using Lykke.MarginTrading.OrderBookService.Contracts;
using Lykke.Service.ClientAccount.Client;
using Lykke.Service.EmailSender;
using Lykke.Service.ExchangeConnector.Client;
using Lykke.SettingsReader;
using Lykke.Snow.Common.Startup;
using MarginTrading.AccountsManagement.Contracts;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Infrastructure;
using MarginTrading.Backend.Services.FakeExchangeConnector;
using MarginTrading.Backend.Services.Settings;
using MarginTrading.Backend.Services.Stubs;
using MarginTrading.Common.Services.Client;
using MarginTrading.SettingsService.Contracts;

namespace MarginTrading.Backend.Modules
{
    public class ExternalServicesModule : Module
    {
        private readonly IReloadingManager<MtBackendSettings> _settings;

        public ExternalServicesModule(IReloadingManager<MtBackendSettings> settings)
        {
            _settings = settings;
        }

        protected override void Load(ContainerBuilder builder)
        {
            if (_settings.CurrentValue.MtBackend.ExchangeConnector == ExchangeConnectorType.RealExchangeConnector)
            {
                var settings = new ExchangeConnectorServiceSettings
                {
                    ServiceUrl = _settings.CurrentValue.MtStpExchangeConnectorClient.ServiceUrl,
                    ApiKey = _settings.CurrentValue.MtStpExchangeConnectorClient.ApiKey
                };
                
                builder.RegisterType<ExchangeConnectorService>()
                .As<IExchangeConnectorService>()
                .WithParameter("settings", settings)
                .SingleInstance();
            }
            if (_settings.CurrentValue.MtBackend.ExchangeConnector == ExchangeConnectorType.FakeExchangeConnector)
            {
                builder.RegisterType<FakeExchangeConnectorService>()
                    .As<IExchangeConnectorService>()
                    .SingleInstance();
            }
            
            #region Client Account Service
            
            if (_settings.CurrentValue.ClientAccountServiceClient != null)
            {
                builder.RegisterLykkeServiceClient(_settings.CurrentValue.ClientAccountServiceClient.ServiceUrl);
                
                builder.RegisterType<ClientAccountService>()
                    .As<IClientAccountService>()
                    .SingleInstance();
            }
            else
            {
                builder.RegisterType<ClientAccountServiceEmptyStub>()
                    .As<IClientAccountService>()
                    .SingleInstance();
            }
            
            #endregion
            
            #region Email Sender

            if (_settings.CurrentValue.EmailSender != null)
            {
                builder.Register<IEmailSender>(ctx =>
                    new EmailSenderClient(_settings.CurrentValue.EmailSender.ServiceUrl, ctx.Resolve<ILog>())
                ).SingleInstance();
            }
            else
            {
                builder.RegisterType<EmailSenderLogStub>().As<IEmailSender>().SingleInstance();
            }
            
            #endregion
            
            #region MT Settings

            var settingsClientGenerator = HttpClientGenerator
                .BuildForUrl(_settings.CurrentValue.SettingsServiceClient.ServiceUrl)
                .WithServiceName<LykkeErrorResponse>(
                    $"MT Settings [{_settings.CurrentValue.SettingsServiceClient.ServiceUrl}]")
                .WithRetriesStrategy(new LinearRetryStrategy(TimeSpan.FromMilliseconds(300), 3))
                .Create();

            builder.RegisterInstance(settingsClientGenerator.Generate<IAssetsApi>())
                .As<IAssetsApi>().SingleInstance();
            
            builder.RegisterInstance(settingsClientGenerator.Generate<IAssetPairsApi>())
                .As<IAssetPairsApi>().SingleInstance();
            
            builder.RegisterInstance(settingsClientGenerator.Generate<ITradingConditionsApi>())
                .As<ITradingConditionsApi>().SingleInstance();
            
            builder.RegisterInstance(settingsClientGenerator.Generate<ITradingInstrumentsApi>())
                .As<ITradingInstrumentsApi>().SingleInstance();
            
            builder.RegisterInstance(settingsClientGenerator.Generate<IScheduleSettingsApi>())
                .As<IScheduleSettingsApi>().SingleInstance();
            
            builder.RegisterInstance(settingsClientGenerator.Generate<ITradingRoutesApi>())
                .As<ITradingRoutesApi>().SingleInstance();
            
            builder.RegisterInstance(settingsClientGenerator.Generate<IServiceMaintenanceApi>())
                .As<IServiceMaintenanceApi>().SingleInstance();

            #endregion

            #region MT Accounts Management

            var accountsClientGenerator = HttpClientGenerator
                .BuildForUrl(_settings.CurrentValue.AccountsManagementServiceClient.ServiceUrl)
                .WithServiceName<LykkeErrorResponse>(
                    $"MT Account Management [{_settings.CurrentValue.AccountsManagementServiceClient.ServiceUrl}]")
                .Create();

            builder.RegisterInstance(accountsClientGenerator.Generate<IAccountsApi>())
                .As<IAccountsApi>().SingleInstance();

            #endregion

            #region OrderBook Service

            var orderBookServiceClientGenerator = HttpClientGenerator
                .BuildForUrl(_settings.CurrentValue.OrderBookServiceClient.ServiceUrl)
                .WithServiceName<LykkeErrorResponse>(
                    $"MT OrderBook Service [{_settings.CurrentValue.OrderBookServiceClient.ServiceUrl}]")
                .Create();

            builder.RegisterInstance(orderBookServiceClientGenerator.Generate<IOrderBookProviderApi>())
                .As<IOrderBookProviderApi>()
                .SingleInstance();

            #endregion OrderBook Service
        }
    }
}