﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Vostok.Applications.AspNetCore.Configuration;
using Vostok.Applications.AspNetCore.Models;
using Vostok.Clusterclient.Context;
using Vostok.Clusterclient.Core.Model;
using Vostok.Context;

namespace Vostok.Applications.AspNetCore.Middlewares
{
    internal class DistributedContextMiddleware
    {
        private readonly RequestDelegate next;
        private readonly DistributedContextSettings settings;

        static DistributedContextMiddleware()
            => FlowingContext.Configuration.RegisterDistributedGlobal(DistributedContextConstants.RequestPriorityGlobalName, new RequestPrioritySerializer());

        public DistributedContextMiddleware(RequestDelegate next, DistributedContextSettings settings)
        {
            this.next = next;
            this.settings = settings;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            FlowingContext.RestoreDistributedProperties(context.Request.Headers[HeaderNames.ContextProperties]);
            FlowingContext.RestoreDistributedGlobals(context.Request.Headers[HeaderNames.ContextGlobals]);

            if (FlowingContext.Globals.Get<RequestPriority?>() == null)
                FlowingContext.Globals.Set<RequestPriority?>(FlowingContext.Globals.Get<IRequestInfo>()?.Priority ?? RequestPriority.Ordinary);

            foreach (var action in settings.AdditionalActions)
                action(context.Request);

            await next(context);
        }
    }
}