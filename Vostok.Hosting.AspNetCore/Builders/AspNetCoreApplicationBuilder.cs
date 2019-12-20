﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Vostok.Commons.Helpers;
using Vostok.Configuration.Microsoft;
using Vostok.Hosting.Abstractions;
using Vostok.Hosting.AspNetCore.Helpers;
using Vostok.Hosting.AspNetCore.Middlewares;
using Vostok.Hosting.AspNetCore.StartupFilters;
using Vostok.Logging.Microsoft;
using Vostok.ServiceDiscovery.Abstractions;

namespace Vostok.Hosting.AspNetCore.Builders
{
    internal class AspNetCoreApplicationBuilder : IVostokAspNetCoreApplicationBuilder
    {
        private readonly Customization<IWebHostBuilder> webHostBuilderCustomization;
        private readonly Customization<FillRequestInfoMiddlewareSettings> fillRequestInfoMiddlewareCustomization;
        private readonly Customization<TracingMiddlewareSettings> tracingMiddlewareCustomization;
        private readonly Customization<LoggingMiddlewareSettings> loggingMiddlewareCustomization;
        private readonly Customization<RestoreDistributedContextMiddlewareSettings> contextMiddlewareCustomization;
        private readonly Customization<PingApiMiddlewareSettings> pingApiMiddlewareCustomization;
        private readonly Customization<VostokLoggerProviderSettings> microsoftLogCustomization;

        private Action<DenyRequestsIfNotInActiveDatacenterMiddlewareSettings> denyRequestsMiddlewareCustomization;

        public AspNetCoreApplicationBuilder()
        {
            webHostBuilderCustomization = new Customization<IWebHostBuilder>();
            fillRequestInfoMiddlewareCustomization = new Customization<FillRequestInfoMiddlewareSettings>();
            tracingMiddlewareCustomization = new Customization<TracingMiddlewareSettings>();
            loggingMiddlewareCustomization = new Customization<LoggingMiddlewareSettings>();
            contextMiddlewareCustomization = new Customization<RestoreDistributedContextMiddlewareSettings>();
            pingApiMiddlewareCustomization = new Customization<PingApiMiddlewareSettings>();
            microsoftLogCustomization = new Customization<VostokLoggerProviderSettings>();
        }

        public static void AddMiddlewares(IWebHostBuilder builder, params IMiddleware[] middlewares)
        {
            middlewares = middlewares.Where(m => m != null).ToArray();

            foreach (var middleware in middlewares)
                builder.ConfigureServices(services => services.AddSingleton(middleware.GetType(), middleware));

            AddStartupFilter(
                builder,
                new AddMiddlewaresStartupFilter(
                    middlewares.Select(m => m.GetType()).ToArray()));
        }

        // CR(iloktionov): 2. Не вижу здесь возможности переопределить DI-контейнер (сделать так, чтобы IServiceProvider был на основе любимого контейнера разработчика).
        // CR(kungurtsev): Насколько я понял, это можно сделать внутри Startup.ConfigureServices
        // CR(kungurtsev): .ConfigureContainer()

        // CR(iloktionov): 4. Тут можно настраивать UseShutdownTimeout (время на drain запросов). Может, будем настраивать? Что там по умолчанию?
        // CR(iloktionov): 5. А есть смысл положить environment из нашей application identity в environment здесь, или это что-то сломает?
        public IHost Build(IVostokHostingEnvironment environment)
        {
            var hostBuilder = Host.CreateDefaultBuilder()
                .ConfigureLogging(
                    loggingBuilder => loggingBuilder
                        .ClearProviders()
                        .AddProvider(CreateMicrosoftLog(environment)))
                .ConfigureAppConfiguration(
                    configurationBuilder => configurationBuilder
                        .AddVostok(environment.ConfigurationSource)
                        .AddVostok(environment.SecretConfigurationSource))
                .ConfigureWebHostDefaults(
                    webHostBuilder =>
                    {
                        ConfigureUrl(webHostBuilder, environment);
                        var urlsBefore = webHostBuilder.GetSetting(WebHostDefaults.ServerUrlsKey);

                        RegisterTypes(webHostBuilder, environment);

                        AddMiddlewares(
                            webHostBuilder,
                            CreateFillRequestInfoMiddleware(),
                            // TODO(kungurtsev): throttling middleware should go here.
                            CreateRestoreDistributedContextMiddleware(),
                            CreateTracingMiddleware(environment),
                            CreateLoggingMiddleware(environment),
                            CreateDenyRequestsIfNotInActiveDatacenterMiddleware(environment),
                            CreatePingApiMiddleware(environment));

                        webHostBuilder.UseKestrel().UseSockets();

                        webHostBuilderCustomization.Customize(webHostBuilder);

                        var urlsAfter = webHostBuilder.GetSetting(WebHostDefaults.ServerUrlsKey);
                        EnsureUrlsNotChanged(urlsBefore, urlsAfter);
                    });

            return hostBuilder.Build();
        }

        private static void ConfigureUrl(IWebHostBuilder builder, IVostokHostingEnvironment environment)
        {
            if (!environment.ServiceBeacon.ReplicaInfo.TryGetUrl(out var url))
                throw new Exception("Port or url should be configured in ServiceBeacon using VostokHostingEnvironmentSetup.");

            builder = builder.UseUrls($"{url.Scheme}://*:{url.Port}/");

            AddStartupFilter(builder, new UrlPathStartupFilter(environment));
        }

        private static void RegisterTypes(IWebHostBuilder builder, IVostokHostingEnvironment environment) =>
            builder.ConfigureServices(
                services =>
                {
                    services
                        .AddSingleton(environment)
                        .AddSingleton(environment.ApplicationIdentity)
                        .AddSingleton(environment.ApplicationLimits)
                        .AddTransient(_ => environment.ApplicationReplicationInfo)
                        .AddSingleton(environment.Metrics)
                        .AddSingleton(environment.Log)
                        .AddSingleton(environment.Tracer)
                        .AddSingleton(environment.HerculesSink)
                        .AddSingleton(environment.ConfigurationSource)
                        .AddSingleton(environment.ConfigurationProvider)
                        .AddSingleton(environment.ClusterConfigClient)
                        .AddSingleton(environment.ServiceBeacon)
                        .AddSingleton(environment.ServiceLocator)
                        .AddSingleton(environment.ContextGlobals)
                        .AddSingleton(environment.ContextProperties)
                        .AddSingleton(environment.ContextConfiguration)
                        .AddSingleton(environment.Datacenters)
                        .AddSingleton(environment.HostExtensions);

                    foreach (var (type, obj) in environment.HostExtensions.GetAll())
                    {
                        services.AddSingleton(type, obj);
                    }
                });

        private static void AddStartupFilter(IWebHostBuilder builder, IStartupFilter startupFilter) =>
            builder.ConfigureServices(
                services =>
                    services
                        .AddTransient(_ => startupFilter));

        private void EnsureUrlsNotChanged(string urlsBefore, string urlsAfter)
        {
            if (urlsAfter.Contains(urlsBefore))
                return;

            throw new Exception(
                "Application url should be configured in ServiceBeacon instead of WebHostBuilder.\n" +
                $"ServiceBeacon url: '{urlsBefore}'. WebHostBuilder urls: '{urlsAfter}'.\n" +
                "To configure application port (without url) use VostokHostingEnvironmentSetup extension: `vostokHostingEnvironmentSetup.SetPort(...)`.\n" +
                "To configure application url use VostokHostingEnvironmentSetup: `vostokHostingEnvironmentSetup.SetupServiceBeacon(serviceBeaconBuilder => serviceBeaconBuilder.SetupReplicaInfo(replicaInfo => replicaInfo.SetUrl(...)))`.");
        }

        #region CreateComponents

        private IMiddleware CreateDenyRequestsIfNotInActiveDatacenterMiddleware(IVostokHostingEnvironment environment)
        {
            if (denyRequestsMiddlewareCustomization == null)
                return null;

            var settings = new DenyRequestsIfNotInActiveDatacenterMiddlewareSettings(environment.Datacenters);
            denyRequestsMiddlewareCustomization(settings);
            return new DenyRequestsIfNotInActiveDatacenterMiddleware(settings, environment.Log);
        }

        private IMiddleware CreateFillRequestInfoMiddleware()
        {
            var settings = new FillRequestInfoMiddlewareSettings();

            fillRequestInfoMiddlewareCustomization.Customize(settings);

            return new FillRequestInfoMiddleware(settings);
        }

        public IMiddleware CreateRestoreDistributedContextMiddleware()
        {
            var settings = new RestoreDistributedContextMiddlewareSettings();

            contextMiddlewareCustomization.Customize(settings);

            return new RestoreDistributedContextMiddleware(settings);
        }

        public IMiddleware CreateTracingMiddleware(IVostokHostingEnvironment environment)
        {
            var settings = new TracingMiddlewareSettings(environment.Tracer);

            tracingMiddlewareCustomization.Customize(settings);

            return new TracingMiddleware(settings);
        }

        public IMiddleware CreateLoggingMiddleware(IVostokHostingEnvironment environment)
        {
            var settings = new LoggingMiddlewareSettings(environment.Log);

            loggingMiddlewareCustomization.Customize(settings);

            return new LoggingMiddleware(settings);
        }

        public IMiddleware CreatePingApiMiddleware(IVostokHostingEnvironment environment)
        {
            var settings = new PingApiMiddlewareSettings();

            pingApiMiddlewareCustomization.Customize(settings);

            return new PingApiMiddleware(settings);
        }

        public ILoggerProvider CreateMicrosoftLog(IVostokHostingEnvironment environment)
        {
            var settings = new VostokLoggerProviderSettings
            {
                IgnoredScopes = new HashSet<string>
                {
                    MicrosoftConstants.ActionLogScope, 
                    MicrosoftConstants.HostingLogScope, 
                    MicrosoftConstants.ConnectionLogScope
                }
            };

            microsoftLogCustomization.Customize(settings);

            return new VostokLoggerProvider(environment.Log, settings);
        }

        #endregion

        #region SetupComponents

        public IVostokAspNetCoreApplicationBuilder SetupWebHost(Action<IWebHostBuilder> setup)
        {
            webHostBuilderCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));
            return this;
        }

        public IVostokAspNetCoreApplicationBuilder DenyRequestsIfNotInActiveDatacenter(int denyResponseCode)
        {
            denyRequestsMiddlewareCustomization = settings => settings.DenyResponseCode = denyResponseCode;
            return this;
        }

        public IVostokAspNetCoreApplicationBuilder SetupFillRequestInfoMiddleware(Action<FillRequestInfoMiddlewareSettings> setup)
        {
            fillRequestInfoMiddlewareCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));

            return this;
        }

        public IVostokAspNetCoreApplicationBuilder SetupRestoreDistributedContextMiddleware(Action<RestoreDistributedContextMiddlewareSettings> setup)
        {
            contextMiddlewareCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));

            return this;
        }

        public IVostokAspNetCoreApplicationBuilder SetupTracingMiddleware(Action<TracingMiddlewareSettings> setup)
        {
            tracingMiddlewareCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));

            return this;
        }

        public IVostokAspNetCoreApplicationBuilder SetupLoggingMiddleware(Action<LoggingMiddlewareSettings> setup)
        {
            loggingMiddlewareCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));

            return this;
        }

        public IVostokAspNetCoreApplicationBuilder SetupPingApiMiddleware(Action<PingApiMiddlewareSettings> setup)
        {
            pingApiMiddlewareCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));

            return this;
        }

        public IVostokAspNetCoreApplicationBuilder SetupMicrosoftLog(Action<VostokLoggerProviderSettings> setup)
        {
            microsoftLogCustomization.AddCustomization(setup ?? throw new ArgumentNullException(nameof(setup)));

            return this;
        }

        #endregion
    }
}