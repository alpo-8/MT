﻿using System;
using System.Collections.Generic;
using System.Threading;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Common.Services.Telemetry;
using Moq;
using NUnit.Framework;

namespace MarginTradingTests
{
    [TestFixture]
    public class TradingSyncContextTests
    {
        private Mock<ITelemetryPublisher> _telemetryPublisherMock;
        private string _actualEventName;
        private string _actualSourceName;
        private IDictionary<string, double> _actualMetrics;
        private IDictionary<string, string> _actualProperties;
        private int _callbacksCount;
        private IContextFactory _contextFactory;

        [SetUp]
        public void SetUp()
        {
            _telemetryPublisherMock = new Mock<ITelemetryPublisher>();
            _telemetryPublisherMock.Setup(p => p.PublishEventMetrics(It.IsNotNull<string>(), It.IsNotNull<string>(),
                    It.IsNotNull<Dictionary<string, double>>(), It.IsAny<Dictionary<string, string>>()))
                .Callback<string, string, IDictionary<string, double>, IDictionary<string, string>>(
                    (eventName, signalSource, metrics, properties) =>
                    {
                        _actualEventName = eventName;
                        _actualSourceName = signalSource;
                        _actualMetrics = metrics;
                        _actualProperties = properties;
                        _callbacksCount++;
                    });

            var settingsMock = new Mock<MarginTradingSettings>();
            settingsMock.SetupGet(s => s.Telemetry)
                .Returns(new TelemetrySettings {LockMetricThreshold = 0});

            _contextFactory = new ContextFactory(_telemetryPublisherMock.Object, settingsMock.Object);
        }

        [TearDown]
        public void TeadDown()
        {
            _callbacksCount = 0;
        }

        [Test]
        public void Check_NestedContext_Works()
        {
            using (_contextFactory.GetWriteSyncContext("test1"))
            {
                Console.WriteLine("Enter context");

                using (_contextFactory.GetWriteSyncContext("test2"))
                {
                    Console.WriteLine("Enter nested context");
                }

                Console.WriteLine(VerifyPublishMetrics(TelemetryConstants.WriteTradingContext, "test2", 2, 0, 0));
                Assert.AreEqual(1, _callbacksCount);
            }

            Console.WriteLine(VerifyPublishMetrics(TelemetryConstants.WriteTradingContext, "test1", 1, 0, 0));
            Assert.AreEqual(2, _callbacksCount);
        }

        [Test]
        public void Check_ReadTiming_Published()
        {
            using (_contextFactory.GetReadSyncContext("test"))
            {
                Console.WriteLine("Enter read context");
                Thread.Sleep(10);
            }

            Console.WriteLine(VerifyPublishMetrics(TelemetryConstants.ReadTradingContext, "test", 1, 0, 10));
            Assert.AreEqual(1, _callbacksCount);
        }

        [Test]
        public void Check_WriteTiming_Published()
        {
            using (_contextFactory.GetWriteSyncContext("test"))
            {
                Console.WriteLine("Enter write context");
                Thread.Sleep(10);
            }

            Console.WriteLine(VerifyPublishMetrics(TelemetryConstants.WriteTradingContext, "test", 1, 0, 10));
            Assert.AreEqual(1, _callbacksCount);
        }

        private string VerifyPublishMetrics(string eventName, string signalSource,
            decimal depth, decimal minPending, decimal minProcessing)
        {
            var actualDepth = _actualMetrics[TelemetryConstants.ContextDepthPropName];
            var actualPending = _actualMetrics[TelemetryConstants.PendingTimePropName];
            var actualProcessed = _actualMetrics[TelemetryConstants.ProcessingTimePropName];

            Assert.AreEqual(eventName, _actualEventName);
            Assert.AreEqual(signalSource, _actualSourceName);
            Assert.AreEqual(depth, actualDepth);
            Assert.GreaterOrEqual(actualPending, minPending);
            Assert.GreaterOrEqual(actualProcessed, minProcessing);
            return $"{eventName} processed by {actualProcessed}. Depth: {actualDepth}. Pending: {actualPending}";
        }
    }
}
