﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Vostok.Applications.AspNetCore.Configuration;
using Vostok.Applications.AspNetCore.Models;
using Vostok.Clusterclient.Core.Model;
using Vostok.Commons.Formatting;
using Vostok.Commons.Time;
using Vostok.Context;
using Vostok.Logging.Abstractions;

namespace Vostok.Applications.AspNetCore.Middlewares
{
    internal class LoggingMiddleware : IMiddleware
    {
        private const int StringBuilderCapacity = 256;

        private readonly LoggingSettings settings;
        private readonly ILog log;

        public LoggingMiddleware(LoggingSettings settings, ILog log)
        {
            this.settings = settings;
            this.log = log;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            LogRequest(context.Request);

            var watch = Stopwatch.StartNew();

            await next(context);

            LogResponse(context.Request, context.Response, watch.Elapsed);
        }

        private void LogRequest(HttpRequest request)
        {
            var requestInfo = FlowingContext.Globals.Get<IRequestInfo>();
            var builder = StringBuilderCache.Acquire(StringBuilderCapacity);

            var addClientIdentity = requestInfo.ClientApplicationIdentity != null;
            var addTimeout = requestInfo.Timeout.HasValue;
            var addBodySize = request.ContentLength > 0L;
            var addHeaders = settings.LogRequestHeaders.IsEnabledForRequest(request);

            var parametersCount = 2 + (addClientIdentity ? 1 : 0) + (addTimeout ? 1 : 0) + (addBodySize ? 1 : 0) + (addHeaders ? 1 : 0);
            var parameters = new object[parametersCount];
            var parametersIndex = 0;

            AppendSegment(builder, parameters, "Received request '{Request}' from", FormatPath(builder, request, settings.LogQueryString), ref parametersIndex);

            if (addClientIdentity)
                AppendSegment(builder, parameters, " '{ClientIdentity}' at", requestInfo.ClientApplicationIdentity, ref parametersIndex);

            AppendSegment(builder, parameters, " '{RequestConnection}'", GetClientConnectionInfo(request), ref parametersIndex);

            if (addTimeout)
                AppendSegment(builder, parameters, " with timeout = {Timeout}", requestInfo.Timeout.Value.ToPrettyString(), ref parametersIndex);

            builder.Append('.');

            if (addBodySize)
                AppendSegment(builder, parameters, " Body size = {BodySize}.", request.ContentLength, ref parametersIndex);

            if (addHeaders)
                AppendSegment(builder, parameters, " Request headers: {RequestHeaders}", FormatHeaders(builder, request.Headers, settings.LogRequestHeaders), ref parametersIndex);

            log.Info(builder.ToString(), parameters);

            StringBuilderCache.Release(builder);
        }

        private void LogResponse(HttpRequest request, HttpResponse response, TimeSpan elapsed)
        {
            var builder = StringBuilderCache.Acquire(StringBuilderCapacity);

            var addBodySize = response.ContentLength > 0;
            var addHeaders = settings.LogResponseHeaders.IsEnabledForRequest(request);

            builder.Append("Response code = {ResponseCode:D} ('{ResponseCode}'). Time = {ElapsedTime}.");

            if (addBodySize)
                builder.Append(" Body size = {BodySize}.");

            if (addHeaders)
                builder.Append(" Response headers: {ResponseHeaders}");

            var logEvent = new LogEvent(LogLevel.Info, PreciseDateTime.Now, builder.ToString())
                .WithProperty("ResponseCode", (ResponseCode) response.StatusCode)
                .WithProperty("ElapsedTime", elapsed.ToPrettyString())
                .WithProperty("ElapsedTimeMs", elapsed.TotalMilliseconds);

            if (addBodySize)
                logEvent = logEvent.WithProperty("BodySize", response.ContentLength);

            if (addHeaders)
                logEvent = logEvent.WithProperty("ResponseHeaders", FormatHeaders(builder, response.Headers, settings.LogResponseHeaders));

            log.Log(logEvent);

            StringBuilderCache.Release(builder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendSegment(StringBuilder builder, object[] parameters, string templateSegment, object parameter, ref int parameterIndex)
        {
            builder.Append(templateSegment);

            parameters[parameterIndex++] = parameter;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetClientConnectionInfo(HttpRequest request)
        {
            var connection = request.HttpContext.Connection;
            return $"{connection.RemoteIpAddress}:{connection.RemotePort}";
        }

        private static string FormatPath(StringBuilder builder, HttpRequest request, LoggingCollectionSettings querySettings)
        {
            return FormatAndRollback(builder, b =>
            {
                b.Append(request.Method);
                b.Append(" ");
                b.Append(request.Path);

                if (querySettings.IsEnabledForRequest(request))
                {
                    if (querySettings.IsEnabledForAllKeys())
                    {
                        b.Append(request.QueryString);
                    }
                    else
                    {
                        var writtenFirst = false;

                        foreach (var pair in request.Query.Where(kvp => querySettings.IsEnabledForKey(kvp.Key)))
                        {
                            if (!writtenFirst)
                            {
                                b.Append('?');
                                writtenFirst = true;
                            }

                            b.Append($"{pair.Key}={pair.Value}");
                        }
                    }
                }
            });
        }

        private static string FormatHeaders(StringBuilder builder, IHeaderDictionary headers, LoggingCollectionSettings settings)
        {
            return FormatAndRollback(builder, b =>
            {
                foreach (var (key, value) in headers)
                {
                    if (!settings.IsEnabledForKey(key))
                        continue;

                    b.AppendLine();
                    b.Append('\t');
                    b.Append(key);
                    b.Append(": ");
                    b.Append(value);
                }
            });
        }

        private static string FormatAndRollback(StringBuilder builder, Action<StringBuilder> format)
        {
            var positionBefore = builder.Length;

            format(builder);

            var result = builder.ToString(positionBefore, builder.Length - positionBefore);

            builder.Length = positionBefore;

            return result;
        }
    }
}
