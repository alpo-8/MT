# MarginTrading.Backend, MarginTrading.AccountMarginEventsBroker #

Margin trading core. Responsible for trading logic.

It consists of 2 applications: 

### MarginTrading.Backend API 
Stateful app - it stores caches in-memory. Single instance only is allowed. It obtains trading commands via API, 
generate events via CQRS and plain RabbitMq exchanges. 
May work with 2 types of storage: MSSQL and Azure (some refactorings needed). 
The app dumps cache data to the blob. 

### MarginTrading.AccountMarginEventsBroker
Broker to pass margin and liquidation events from message queue to storage.

## How to use MarginTrading.Backend API in prod env? ##

1. Pull "mt-trading-core" docker image with a corresponding tag.
2. Configure environment variables according to "Environment variables" section.
3. Put secrets.json with endpoint data including the certificate:
```json
"Kestrel": {
  "EndPoints": {
    "HttpsInlineCertFile": {
      "Url": "https://*:5130",
      "Certificate": {
        "Path": "<path to .pfx file>",
        "Password": "<certificate password>"
      }
    }
  }
}
```
4. Initialize all dependencies.
5. Run.

## How to run for debug? ##

1. Clone repo to some directory.
2. In MarginTrading.Backend root create a appsettings.dev.json with settings.
3. Add environment variable "SettingsUrl": "appsettings.dev.json".
4. VPN to a corresponding env must be connected and all dependencies must be initialized.
5. Run.

### Dependencies ###
* Settings Service must be up and running in order to read assets, asset pairs, trading schedule, trading instruments, 
schedule settings, trading routes.
* Account management must be up and running in order to initialize account state.
* Gavel or other ExchangeConnectorService implementation must be up and running in order to execute orders.
* Azure storage or MSSQL Server db instance must be available depending on settings.
* RabbitMQ must be up and running. 

### Configuration ###

Kestrel configuration may be passed through appsettings.json, secrets or environment.
All variables and value constraints are default. For instance, to set host URL the following env variable may be set:
```json
{
    "Kestrel__EndPoints__Http__Url": "http://*:5030"
}
```

### Environment variables ###

* *RESTART_ATTEMPTS_NUMBER* - number of restart attempts. If not set int.MaxValue is used.
* *RESTART_ATTEMPTS_INTERVAL_MS* - interval between restarts in milliseconds. If not set 10000 is used.
* *SettingsUrl* - defines URL of remote settings or path for local settings.

### Settings ###

Settings schema is:

```json
{
  "AccountsManagementServiceClient": {
    "ServiceUrl": "http://mt-account-management.mt.svc.cluster.local"
  },
  "Jobs": {
    "NotificationsHubName": "",
    "NotificationsHubConnectionString": ""
  },
  "MtBackend": {
    "ApiKey": "MT Core backend api key",
    "MtRabbitMqConnString": "amqp://login:password@rabbit-mt.mt.svc.cluster.local:5672",
    "Db": {
      "StorageMode": "SqlServer",
      "LogsConnString": "logs connection string",
      "MarginTradingConnString": "date connection string",
      "HistoryConnString": "history connection string",
      "StateConnString": "state connection string",
      "SqlConnectionString": "sql connection string"
    },
    "RabbitMqQueues": {
      "OrderHistory": {
        "ExchangeName": "lykke.mt.orderhistory"
      },
      "OrderRejected": {
        "ExchangeName": "lykke.mt.orderrejected"
      },
      "OrderbookPrices": {
        "ExchangeName": "lykke.mt.pricefeed"
      },
      "AccountChanged": {
        "ExchangeName": "lykke.mt.account.changed"
      },
      "AccountStopout": {
        "ExchangeName": "lykke.mt.account.stopout"
      },
      "AccountMarginEvents": {
        "ExchangeName": "lykke.mt.account.marginevents"
      },
      "AccountStats": {
        "ExchangeName": "lykke.mt.account.stats"
      },
      "Trades": {
        "ExchangeName": "lykke.mt.trades"
      },
      "PositionHistory": {
        "ExchangeName": "lykke.mt.position.history"
      },
      "ExternalOrder": {
        "ExchangeName": "lykke.stpexchangeconnector.trades"
      },
      "MarginTradingEnabledChanged": {
        "ExchangeName": "lykke.mt.enabled.changed"
      },
      "SettingsChanged": {
        "ExchangeName": "MtCoreSettingsChanged"
      }
    },
    "FxRateRabbitMqSettings": {
      "ConnectionString": "amqp://login:pwd@rabbit-mt.mt.svc.cluster.local:5672",
      "ExchangeName": "lykke.stpexchangeconnector.fxRates"
    },
    "StpAggregatorRabbitMqSettings": {
      "ConnectionString": "amqp://login:pwd@rabbit-mt.mt.svc.cluster.local:5672",
      "ExchangeName": "lykke.exchangeconnector.orderbooks",
      "ConsumerCount": 10
    },
    "BlobPersistence": {
      "QuotesDumpPeriodMilliseconds": 3400000,
      "FxRatesDumpPeriodMilliseconds": 3500000,
      "OrderbooksDumpPeriodMilliseconds": 3600000,
      "OrdersDumpPeriodMilliseconds": 600000
    },
    "RequestLoggerSettings": {
      "Enabled": false,
      "MaxPartSize": 2048
    },
    "Telemetry": {
      "LockMetricThreshold": 10
    },
    "ReportingEquivalentPricesSettings": [
      {
        "LegalEntity": "Default",
        "EquivalentAsset": "EUR"
      },
      {
        "LegalEntity": "UNKNOWN",
        "EquivalentAsset": "USD"
      }
    ],
    "UseAzureIdentityGenerator": false,
    "WriteOperationLog": true,
    "UseSerilog": false,
    "ExchangeConnector": "FakeExchangeConnector",
    "MaxMarketMakerLimitOrderAge": 3000000,
    "Cqrs": {
      "ConnectionString": "amqp://login:pws@rabbit-mt.mt.svc.cluster.local:5672",
      "RetryDelay": "00:00:02",
      "EnvironmentName": "env name"
    },
    "SpecialLiquidation": {
      "Enabled": true,
      "FakePrice": 5,
      "PriceRequestTimeoutSec": 600,
      "RetryTimeout": "00:01:00",
      "VolumeThreshold": 1000,
      "VolumeThresholdCurrency": "EUR"
    },
    "ChaosKitty": {
      "StateOfChaos": 0
    }
  },
  "MtStpExchangeConnectorClient": {
    "ServiceUrl": "http://gavel.mt.svc.cluster.local:5019",
    "ApiKey": "key"
  },
  "SettingsServiceClient": {
    "ServiceUrl": "http://mt-settings-service.mt.svc.cluster.local"
  }
}
```
