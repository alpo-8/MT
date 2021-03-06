using System;
using System.Collections.Generic;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Common.Log;
using FluentScheduler;
using JetBrains.Annotations;
using Lykke.AzureQueueIntegration;
using Lykke.Common;
using Lykke.Common.ApiLibrary.Swagger;
using Lykke.Logs;
using Lykke.Logs.MsSql;
using Lykke.Logs.MsSql.Repositories;
using Lykke.Logs.Serilog;
using Lykke.SettingsReader;
using Lykke.SlackNotification.AzureQueue;
using Lykke.SlackNotifications;
using MarginTrading.AzureRepositories;
using MarginTrading.Backend.Core;
using MarginTrading.Backend.Core.Services;
using MarginTrading.Backend.Core.Settings;
using MarginTrading.Backend.Filters;
using MarginTrading.Backend.Infrastructure;
using MarginTrading.Backend.Middleware;
using MarginTrading.Backend.Modules;
using MarginTrading.Backend.Services;
using MarginTrading.Backend.Services.AssetPairs;
using MarginTrading.Backend.Services.Caches;
using MarginTrading.Backend.Services.Infrastructure;
using MarginTrading.Backend.Services.Modules;
using MarginTrading.Backend.Services.Quotes;
using MarginTrading.Backend.Services.Scheduling;
using MarginTrading.Backend.Services.Settings;
using MarginTrading.Backend.Services.Stp;
using MarginTrading.Backend.Services.Stubs;
using MarginTrading.Backend.Services.TradingConditions;
using MarginTrading.Common.Extensions;
using MarginTrading.Common.Modules;
using MarginTrading.Common.Services;
using MarginTrading.SqlRepositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using StackExchange.Redis;
using GlobalErrorHandlerMiddleware = MarginTrading.Backend.Middleware.GlobalErrorHandlerMiddleware;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

#pragma warning disable 1591

namespace MarginTrading.Backend
{
    public class Startup
    {
        public IConfigurationRoot Configuration { get; }
        public IHostingEnvironment Environment { get; }
        public IContainer ApplicationContainer { get; set; }

        public Startup(IHostingEnvironment env)
        {
            Configuration = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddSerilogJson(env)
                .AddEnvironmentVariables()
                .Build();

            Environment = env;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            var loggerFactory = new LoggerFactory()
                .AddConsole(LogLevel.Error)
                .AddDebug(LogLevel.Error);

            services.AddSingleton(loggerFactory);
            services.AddLogging();
            services.AddSingleton(Configuration);
            services.AddMvc()
            .AddJsonOptions(options =>
            {
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                options.SerializerSettings.Converters.Add(new StringEnumConverter());
            });
            services.AddScoped<MarginTradingEnabledFilter>();
            services.AddAuthentication(KeyAuthOptions.AuthenticationScheme)
                .AddScheme<KeyAuthOptions, KeyAuthHandler>(KeyAuthOptions.AuthenticationScheme, "", options => { });

            services.AddSwaggerGen(options =>
            {
                options.DefaultLykkeConfiguration("v1", $"MarginTradingEngine_Api_{Configuration.ServerType()}");
                options.OperationFilter<ApiKeyHeaderOperationFilter>();
            });

            var builder = new ContainerBuilder();

            var mtSettings = Configuration.LoadSettings<MtBackendSettings>(
                    throwExceptionOnCheckError: !Configuration.NotThrowExceptionsOnServiceValidation())
                .Nested(s =>
                {
                    s.MtBackend.Env = Configuration.ServerType();
                    return s;
                });

            SetupLoggers(Configuration, services, mtSettings);

            var deduplicationService = RunHealthChecks(mtSettings.CurrentValue.MtBackend);
            builder.RegisterInstance(deduplicationService).AsSelf().As<IDisposable>().SingleInstance();

            RegisterModules(builder, mtSettings, Environment);

            builder.Populate(services);

            ApplicationContainer = builder.Build();

            MtServiceLocator.FplService = ApplicationContainer.Resolve<IFplService>();
            MtServiceLocator.AccountUpdateService = ApplicationContainer.Resolve<IAccountUpdateService>();
            MtServiceLocator.AccountsCacheService = ApplicationContainer.Resolve<IAccountsCacheService>();
            MtServiceLocator.SwapCommissionService = ApplicationContainer.Resolve<ICommissionService>();
            
            ApplicationContainer.Resolve<IScheduleSettingsCacheService>()
                .UpdateAllSettingsAsync().GetAwaiter().GetResult();

            InitializeJobs();

            return new AutofacServiceProvider(ApplicationContainer);
        }

        [UsedImplicitly]
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory,
            IApplicationLifetime appLifetime)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            
            app.UseMiddleware<GlobalErrorHandlerMiddleware>();
            app.UseMiddleware<MaintenanceModeMiddleware>();
            app.UseAuthentication();
            app.UseMvc();

            app.UseSwagger(c =>
            {
                c.PreSerializeFilters.Add((swagger, httpReq) => swagger.Host = httpReq.Host.Value);
            });
            app.UseSwaggerUI(a => a.SwaggerEndpoint("/swagger/v1/swagger.json", "Trading Engine API Swagger"));

            appLifetime.ApplicationStopped.Register(() => ApplicationContainer.Dispose());

            var application = app.ApplicationServices.GetService<Application>();

            appLifetime.ApplicationStarted.Register(() =>
            {
                LogLocator.CommonLog?.WriteMonitorAsync("", "", $"{Configuration.ServerType()} Started");
            });

            appLifetime.ApplicationStopping.Register(() =>
                {
                    LogLocator.CommonLog?.WriteMonitorAsync("", "", $"{Configuration.ServerType()} Terminating");
                    application.StopApplication();
                }
            );
        }

        private static void RegisterModules(ContainerBuilder builder, IReloadingManager<MtBackendSettings> mtSettings,
            IHostingEnvironment environment)
        {
            var settings = mtSettings.Nested(x => x.MtBackend);
            
            builder.RegisterModule(new BaseServicesModule(mtSettings.CurrentValue, LogLocator.CommonLog));
            builder.RegisterModule(new BackendSettingsModule(mtSettings));
            builder.RegisterModule(new BackendRepositoriesModule(settings, LogLocator.CommonLog));
            builder.RegisterModule(new EventModule());
            builder.RegisterModule(new CacheModule());
            builder.RegisterModule(new ManagersModule());
            builder.RegisterModule(new ServicesModule());
            builder.RegisterModule(new BackendServicesModule(mtSettings.CurrentValue, settings.CurrentValue,
                environment, LogLocator.CommonLog));
            builder.RegisterModule(new MarginTradingCommonModule());
            builder.RegisterModule(new ExternalServicesModule(mtSettings));
            builder.RegisterModule(new BackendMigrationsModule());
            builder.RegisterModule(new CqrsModule(settings.CurrentValue.Cqrs, LogLocator.CommonLog, settings.CurrentValue));

            builder.RegisterBuildCallback(async c =>
            {
                // note the order here is important!
                c.Resolve<TradingInstrumentsManager>();
                c.Resolve<OrderBookSaveService>();
                await c.Resolve<IExternalOrderbookService>().InitializeAsync();
                c.Resolve<QuoteCacheService>().Start();
                c.Resolve<FxRateCacheService>();
                c.Resolve<AccountManager>();
                c.Resolve<OrderCacheManager>();
                c.Resolve<PendingOrdersCleaningService>();
            });
        }

        private static void SetupLoggers(IConfiguration configuration, IServiceCollection services,
            IReloadingManager<MtBackendSettings> mtSettings)
        {
            var settings = mtSettings.Nested(x => x.MtBackend);
            const string requestsLogName = "MarginTradingBackendRequestsLog";
            const string logName = "MarginTradingBackendLog";
            var consoleLogger = new LogToConsole();

            #region Logs settings validation

            if (!settings.CurrentValue.UseSerilog && string.IsNullOrWhiteSpace(settings.CurrentValue.Db.LogsConnString))
            {
                throw new Exception("Either UseSerilog must be true or LogsConnString must be set");
            }

            #endregion Logs settings validation
            
            #region Slack registration

            IMtSlackNotificationsSender slackService = null;

            if (mtSettings.CurrentValue.SlackNotifications != null)
            {
                var azureQueue = new AzureQueueSettings
                {
                    ConnectionString = mtSettings.CurrentValue.SlackNotifications.AzureQueue.ConnectionString,
                    QueueName = mtSettings.CurrentValue.SlackNotifications.AzureQueue.QueueName
                };

                var commonSlackService =
                    services.UseSlackNotificationsSenderViaAzureQueue(azureQueue, consoleLogger);

                slackService =
                    new MtSlackNotificationsSender(commonSlackService, "MT Backend", settings.CurrentValue.Env);
            }
            else
            {
                slackService =
                    new MtSlackNotificationsSenderLogStub("MT Backend", settings.CurrentValue.Env, consoleLogger);
            }

            services.AddSingleton<ISlackNotificationsSender>(slackService);
            services.AddSingleton<IMtSlackNotificationsSender>(slackService);

            #endregion Slack registration

            if (settings.CurrentValue.UseSerilog)
            {
                LogLocator.RequestsLog = LogLocator.CommonLog = new SerilogLogger(typeof(Startup).Assembly, configuration);
            }
            else if (settings.CurrentValue.Db.StorageMode == StorageMode.SqlServer)
            {
                LogLocator.RequestsLog = new AggregateLogger(
                    new LogToSql(new SqlLogRepository(requestsLogName,
                        settings.CurrentValue.Db.LogsConnString)),
                    new LogToConsole());

                LogLocator.CommonLog = new AggregateLogger(
                    new LogToSql(new SqlLogRepository(logName,
                        settings.CurrentValue.Db.LogsConnString)),
                    new LogToConsole());
            }
            else if (settings.CurrentValue.Db.StorageMode == StorageMode.Azure)
            {
                LogLocator.RequestsLog = services.UseLogToAzureStorage(settings.Nested(s => s.Db.LogsConnString),
                    slackService, requestsLogName, consoleLogger);

                LogLocator.CommonLog = services.UseLogToAzureStorage(settings.Nested(s => s.Db.LogsConnString),
                    slackService, logName, consoleLogger);
            }
        }

        /// <summary>
        /// Initialize scheduled jobs. Each job will start in time with dispersion of 100ms.
        /// </summary>
        private void InitializeJobs()
        {   
            var registry = new Registry();
            
            registry.Schedule<ScheduleSettingsCacheWarmUpJob>()
                .WithName(nameof(ScheduleSettingsCacheWarmUpJob)).ToRunEvery(1).Days().At(0, 0);
         
            JobManager.UseUtcTime();   
            JobManager.Initialize(registry);
            
            ApplicationContainer.Resolve<IOvernightMarginService>().ScheduleNext();
        }

        private StartupDeduplicationService RunHealthChecks(MarginTradingSettings marginTradingSettings)
        {
            var deduplicationService = new StartupDeduplicationService(Environment, LogLocator.CommonLog, 
                marginTradingSettings);
            deduplicationService
                .HoldLock();
            
            new StartupQueuesCheckerService(marginTradingSettings)
                .Check();

            return deduplicationService;
        }
    }
}