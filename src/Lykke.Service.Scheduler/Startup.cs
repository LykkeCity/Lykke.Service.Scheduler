using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using AzureStorage.Tables;
using Common.Log;
using Lykke.Common.ApiLibrary.Middleware;
using Lykke.Common.ApiLibrary.Swagger;
using Lykke.Logs;
using Lykke.Service.Scheduler.Core.Services;
using Lykke.Service.Scheduler.Core.Settings;
using Lykke.Service.Scheduler.Modules;
using Lykke.SettingsReader;
using Lykke.SlackNotification.AzureQueue;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz.Impl;
using Quartz;
using Lykke.Service.Scheduler.Core.Settings.ServiceSettings;

namespace Lykke.Service.Scheduler
{
    public class Startup
    {
        public IHostingEnvironment Environment { get; }
        public IContainer ApplicationContainer { get; private set; }
        public IConfigurationRoot Configuration { get; }
        public ILog Log { get; private set; }

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddEnvironmentVariables();
            Configuration = builder.Build();

            Environment = env;
        }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            try
            {
                services.AddMvc()
                    .AddJsonOptions(options =>
                    {
                        options.SerializerSettings.ContractResolver =
                            new Newtonsoft.Json.Serialization.DefaultContractResolver();
                    });

                services.AddSwaggerGen(options =>
                {
                    options.DefaultLykkeConfiguration("v1", "Scheduler API");
                });

                var builder = new ContainerBuilder();
                var reloadableAppSettings = Configuration.LoadSettings<AppSettings>();
                var appSettings = reloadableAppSettings.CurrentValue;

                Log = CreateLogWithSlack(services, reloadableAppSettings);

                builder.RegisterModule(new ServiceModule(reloadableAppSettings.Nested(x => x.SchedulerService), Log));
                builder.Populate(services);
                ApplicationContainer = builder.Build();

                var schedulerFactory = new StdSchedulerFactory();
                var scheduler = schedulerFactory.GetScheduler().Result;
                scheduler.Start().Wait();

                int scNum = 0;
                foreach (ScheduledCall sc in appSettings.SchedulerService.ScheduledCalls)
                {
                    scNum++;

                    var jobKey = new JobKey("StartService" + scNum);
                    var triggerKey = new TriggerKey("StartServiceCron" + scNum);

                    var job = scheduler.CheckExists(jobKey).Result ?
                        scheduler.GetJobDetail(jobKey).Result :
                        JobBuilder.Create<CallServiceJob>()
                            .WithIdentity(jobKey)
                            .Build();

                    var trigger = scheduler.CheckExists(triggerKey).Result ?
                        scheduler.GetTrigger(triggerKey).Result :
                        TriggerBuilder.Create()
                        .WithIdentity(triggerKey)
                        .StartNow()
                        .WithCronSchedule(sc.CronExpression)
                        .Build();

                    job.JobDataMap["Url"] = sc.Url;

                    scheduler.ScheduleJob(job, trigger).Wait();
                }


                return new AutofacServiceProvider(ApplicationContainer);
            }
            catch (Exception ex)
            {
                Log?.WriteFatalErrorAsync(nameof(Startup), nameof(ConfigureServices), "", ex);
                throw;
            }
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IApplicationLifetime appLifetime)
        {
            try
            {
                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }

                app.UseLykkeMiddleware("Scheduler", ex => new {Message = "Technical problem"});

                app.UseMvc();
                app.UseSwagger();
                app.UseSwaggerUi();
                app.UseStaticFiles();

                appLifetime.ApplicationStarted.Register(StartApplication);
                appLifetime.ApplicationStopping.Register(StopApplication);
                appLifetime.ApplicationStopped.Register(CleanUp);
            }
            catch (Exception ex)
            {
                Log?.WriteFatalErrorAsync(nameof(Startup), nameof(ConfigureServices), "", ex);
                throw;
            }
        }

        private void StartApplication()
        {
            try
            {
                // NOTE: Service not yet recieve and process requests here

                ApplicationContainer.Resolve<IStartupManager>().StartAsync().Wait();
            }
            catch (Exception ex)
            {
                Log?.WriteFatalErrorAsync(nameof(Startup), nameof(StartApplication), "", ex);
                throw;
            }
        }

        private void StopApplication()
        {
            try
            {
                // NOTE: Service still can recieve and process requests here, so take care about it if you add logic here.

                ApplicationContainer.Resolve<IShutdownManager>().StopAsync().Wait();
            }
            catch (Exception ex)
            {
                Log?.WriteFatalErrorAsync(nameof(Startup), nameof(StopApplication), "", ex);
                throw;
            }
        }

        private void CleanUp()
        {
            try
            {
                // NOTE: Service can't recieve and process requests here, so you can destroy all resources

                ApplicationContainer.Dispose();
            }
            catch (Exception ex)
            {
                Log?.WriteFatalErrorAsync(nameof(Startup), nameof(CleanUp), "", ex);
                (Log as IDisposable)?.Dispose();
                throw;
            }
        }

        private static ILog CreateLogWithSlack(IServiceCollection services, IReloadingManager<AppSettings> settings)
        {
            var consoleLogger = new LogToConsole();
            var aggregateLogger = new AggregateLogger();

            aggregateLogger.AddLog(consoleLogger);

            // Creating slack notification service, which logs own azure queue processing messages to aggregate log
            var slackService = services.UseSlackNotificationsSenderViaAzureQueue(new AzureQueueIntegration.AzureQueueSettings
            {
                ConnectionString = settings.CurrentValue.SlackNotifications.AzureQueue.ConnectionString,
                QueueName = settings.CurrentValue.SlackNotifications.AzureQueue.QueueName
            }, aggregateLogger);

            var dbLogConnectionStringManager = settings.Nested(x => x.SchedulerService.Db.LogsConnString);
            var dbLogConnectionString = dbLogConnectionStringManager.CurrentValue;

            // Creating azure storage logger, which logs own messages to concole log
            if (!string.IsNullOrEmpty(dbLogConnectionString) && !(dbLogConnectionString.StartsWith("${") && dbLogConnectionString.EndsWith("}")))
            {
                var persistenceManager = new LykkeLogToAzureStoragePersistenceManager(
                    AzureTableStorage<LogEntity>.Create(dbLogConnectionStringManager, "SchedulerLog", consoleLogger),
                    consoleLogger);

                var slackNotificationsManager = new LykkeLogToAzureSlackNotificationsManager(slackService, consoleLogger);

                var azureStorageLogger = new LykkeLogToAzureStorage(
                    persistenceManager,
                    slackNotificationsManager,
                    consoleLogger);

                azureStorageLogger.Start();

                aggregateLogger.AddLog(azureStorageLogger);
            }

            return aggregateLogger;
        }
    }
}
