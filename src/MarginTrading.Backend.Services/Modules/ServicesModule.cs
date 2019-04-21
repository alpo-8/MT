﻿using Autofac;
using Common.Log;
using Autofac.Features.Variance;
using Lykke.Common.Chaos;
using Lykke.SettingsReader;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.MatchingEngines;
using MarginTrading.Backend.Core.Orderbooks;
using MarginTrading.Backend.Core.Services;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Services.AssetPairs;
using MarginTrading.Backend.Services.Events;
using MarginTrading.Backend.Services.EventsConsumers;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Backend.Services.MatchingEngines;
using MarginTrading.Backend.Services.Quotes;
using MarginTrading.Backend.Services.Services;
using MarginTrading.Backend.Services.Stp;
using MarginTrading.Backend.Services.TradingConditions;
using MarginTrading.Common.RabbitMq;
using MarginTrading.Common.Services;
using MarginTrading.Common.Services.Telemetry;

namespace MarginTrading.Backend.Services.Modules
{
	public class ServicesModule : Module
	{
		protected override void Load(ContainerBuilder builder)
		{
			builder.RegisterType<QuoteCacheService>()
				.AsSelf()
				.As<IQuoteCacheService>()
				.As<IEventConsumer<BestPriceChangeEventArgs>>()
				.SingleInstance();
 
			builder.RegisterType<FxRateCacheService>() 
				.AsSelf()
				.As<IFxRateCacheService>()
				.SingleInstance()
				.OnActivated(args => args.Instance.Start()); 

			builder.RegisterType<FplService>()
				.As<IFplService>()
				.SingleInstance();

			builder.RegisterType<TradingConditionsCacheService>()
				.AsSelf()
				.As<ITradingConditionsCacheService>()
				.SingleInstance();

			builder.RegisterType<TradingInstrumentsCacheService>()
				.AsSelf()
				.As<ITradingInstrumentsCacheService>()
				.As<IOvernightMarginParameterContainer>()
				.SingleInstance();

			builder.RegisterType<AccountUpdateService>()
				.As<IAccountUpdateService>()
				.SingleInstance();

			builder.RegisterType<ValidateOrderService>()
				.As<IValidateOrderService>()
				.SingleInstance();

			builder.RegisterType<CommissionService>()
				.As<ICommissionService>()
				.SingleInstance();

			//TODO: rework ME registrations
			builder.RegisterType<MarketMakerMatchingEngine>()
				.As<IMarketMakerMatchingEngine>()
				.WithParameter(TypedParameter.From(MatchingEngineConstants.DefaultMm))
				.SingleInstance();

			builder.RegisterType<StpMatchingEngine>()
				.As<IStpMatchingEngine>()
				.WithParameter(TypedParameter.From(MatchingEngineConstants.DefaultStp))
				.SingleInstance();

			builder.RegisterType<TradingEngine>()
				.As<ITradingEngine>()
				.As<IEventConsumer<BestPriceChangeEventArgs>>()
				.As<IEventConsumer<FxBestPriceChangeEventArgs>>()
				.SingleInstance();

			builder.RegisterType<MarginCallConsumer>()
				.As<IEventConsumer<MarginCallEventArgs>>()
				//.As<IEventConsumer<OrderPlacedEventArgs>>()
				.SingleInstance();

			builder.RegisterType<StopOutConsumer>()
				.As<IEventConsumer<StopOutEventArgs>>()
				.SingleInstance();

			builder.RegisterSource(new ContravariantRegistrationSource());
			builder.RegisterType<OrderStateConsumer>()
				.As<IEventConsumer<OrderPlacedEventArgs>>()
				.As<IEventConsumer<OrderExecutedEventArgs>>()
				.As<IEventConsumer<OrderCancelledEventArgs>>()
				.As<IEventConsumer<OrderChangedEventArgs>>()
				.As<IEventConsumer<OrderExecutionStartedEventArgs>>()
				.As<IEventConsumer<OrderActivatedEventArgs>>()
				.As<IEventConsumer<OrderRejectedEventArgs>>()
				.SingleInstance();

			builder.RegisterType<TradesConsumer>()
				.As<IEventConsumer<OrderExecutedEventArgs>>()
				.SingleInstance();
			
			builder.RegisterType<PositionsConsumer>()
				.As<IEventConsumer<OrderExecutedEventArgs>>()
				.SingleInstance();

			builder.RegisterType<CfdCalculatorService>()
				.As<ICfdCalculatorService>()
				.SingleInstance();

			builder.RegisterType<OrderBookList>()
				.AsSelf()
				.SingleInstance();

			builder.RegisterType<ExternalOrderbookService>()
				.As<IExternalOrderbookService>()
				.SingleInstance();

			builder.RegisterType<MarketMakerService>()
				.AsSelf()
				.SingleInstance();

			builder.RegisterType<MarginTradingEnabledCacheService>()
				.As<IMarginTradingSettingsCacheService>()
				.SingleInstance();

			builder.RegisterType<MatchingEngineRouter>()
				.As<IMatchingEngineRouter>()
				.SingleInstance();

			builder.RegisterType<MatchingEngineRoutesCacheService>()
				.As<IMatchingEngineRoutesCacheService>()
				.AsSelf()
				.SingleInstance();

			builder.RegisterType<AssetPairDayOffService>()
				.As<IAssetPairDayOffService>()
				.SingleInstance();

			builder.RegisterType<TelemetryPublisher>()
				.As<ITelemetryPublisher>()
				.SingleInstance();

			builder.RegisterType<ContextFactory>()
				.As<IContextFactory>()
				.SingleInstance();

			builder.Register(c =>
				{
					var settings = c.Resolve<IReloadingManager<MarginTradingSettings>>();
					return new RabbitMqService(c.Resolve<ILog>(), c.Resolve<IConsole>(),
						settings.Nested(s => s.Db.StateConnString), settings.CurrentValue.Env);
				})
				.As<IRabbitMqService>()
				.SingleInstance();

			builder.RegisterType<ScheduleSettingsCacheService>()
				.As<IScheduleSettingsCacheService>()
				.SingleInstance();

			builder.RegisterType<AlertSeverityLevelService>()
				.As<IAlertSeverityLevelService>()
				.SingleInstance();

			builder.RegisterType<MarginTradingEnablingService>()
				.As<IMarginTradingEnablingService>()
				.As<IStartable>()
				.SingleInstance();

			builder.RegisterType<ReportService>()
				.As<IReportService>()
				.SingleInstance();

			builder.RegisterType<OvernightMarginService>()
				.As<IOvernightMarginService>()
				.SingleInstance();
		}
	}
}
