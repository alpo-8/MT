﻿using System;
using System.Threading.Tasks;
using Common.Log;
using Lykke.RabbitMqBroker;
using Lykke.RabbitMqBroker.Subscriber;
using Lykke.SlackNotifications;
using MarginTrading.BrokerBase.Settings;
using MarginTrading.Common.Enums;
using MarginTrading.Common.RabbitMq;

namespace MarginTrading.BrokerBase
{
    public abstract class BrokerApplicationBase<TMessage> : IBrokerApplication
    {
        private readonly ILog _logger;
        private readonly ISlackNotificationsSender _slackNotificationsSender;
        protected readonly CurrentApplicationInfo ApplicationInfo;
        private RabbitMqSubscriber<TMessage> _connector;

        protected abstract BrokerSettingsBase Settings { get; }
        protected abstract string ExchangeName { get; }
        
        private RabbitMqSubscriptionSettings GetRabbitMqSubscriptionSettings()
        {
            return new RabbitMqSubscriptionSettings
            {
                ConnectionString = Settings.MtRabbitMqConnString,
                QueueName = QueueHelper.BuildQueueName(ExchangeName, Settings.Env),
                ExchangeName = ExchangeName,
                IsDurable = true
            };
        }

        protected abstract Task HandleMessage(TMessage message);

        protected BrokerApplicationBase(ILog logger, ISlackNotificationsSender slackNotificationsSender,
            CurrentApplicationInfo applicationInfo)
        {
            _logger = logger;
            _slackNotificationsSender = slackNotificationsSender;
            ApplicationInfo = applicationInfo;
        }

        public virtual void Run()
        {
            WriteInfoToLogAndSlack("Starting listening exchange " + ExchangeName);
            
            try
            {
                var settings = GetRabbitMqSubscriptionSettings();
                _connector =
                    new RabbitMqSubscriber<TMessage>(settings,
                            new ResilientErrorHandlingStrategy(_logger, settings, TimeSpan.FromSeconds(1)))
                        .SetMessageDeserializer(new JsonMessageDeserializer<TMessage>())
                        .SetMessageReadStrategy(new MessageReadWithTemporaryQueueStrategy())
                        .Subscribe(HandleMessage)
                        .SetLogger(_logger)
                        .Start();

                WriteInfoToLogAndSlack("Broker listening queue " + settings.QueueName);
            }
            catch (Exception ex)
            {
                _logger.WriteErrorAsync(ApplicationInfo.ApplicationName, "Application.RunAsync", null, ex).GetAwaiter()
                    .GetResult();
            }
        }

        public void StopApplication()
        {
            Console.WriteLine($"Closing {ApplicationInfo.ApplicationName}...");
            WriteInfoToLogAndSlack("Stopping listening exchange " + ExchangeName);
            _connector.Stop();
        }

        private void WriteInfoToLogAndSlack(string infoMessage)
        {
            _logger.WriteInfoAsync(ApplicationInfo.ApplicationName, null, null, infoMessage);
            _slackNotificationsSender.SendAsync(ChannelTypes.MarginTrading, ApplicationInfo.ApplicationName,
                infoMessage);
        }
    }
}