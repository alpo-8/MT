﻿using Autofac;
using MarginTrading.Backend.Services.Events;

namespace MarginTrading.Backend.Services.Modules
{
    public class EventModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            builder.RegisterType<EventChannel<BestPriceChangeEventArgs>>()
                .As<IEventChannel<BestPriceChangeEventArgs>>()
                .SingleInstance();
            
            builder.RegisterType<EventChannel<FxBestPriceChangeEventArgs>>()
                .As<IEventChannel<FxBestPriceChangeEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<MarginCallEventArgs>>()
                .As<IEventChannel<MarginCallEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<OrderCancelledEventArgs>>()
                .As<IEventChannel<OrderCancelledEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<OrderPlacedEventArgs>>()
                .As<IEventChannel<OrderPlacedEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<OrderExecutedEventArgs>>()
                .As<IEventChannel<OrderExecutedEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<StopOutEventArgs>>()
                .As<IEventChannel<StopOutEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<PositionUpdateEventArgs>>()
                .As<IEventChannel<PositionUpdateEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<AccountBalanceChangedEventArgs>>()
                .As<IEventChannel<AccountBalanceChangedEventArgs>>()
                .SingleInstance();
            
            builder.RegisterType<EventChannel<OrderChangedEventArgs>>()
                .As<IEventChannel<OrderChangedEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<OrderExecutionStartedEventArgs>>()
                .As<IEventChannel<OrderExecutionStartedEventArgs>>()
                .SingleInstance();

            builder.RegisterType<EventChannel<OrderActivatedEventArgs>>()
                .As<IEventChannel<OrderActivatedEventArgs>>()
                .SingleInstance();
            
            builder.RegisterType<EventChannel<OrderRejectedEventArgs>>()
                .As<IEventChannel<OrderRejectedEventArgs>>()
                .SingleInstance();
            
            builder.RegisterType<EventChannel<LiquidationEndEventArgs>>()
                .As<IEventChannel<LiquidationEndEventArgs>>()
                .SingleInstance();
        }
    }
}