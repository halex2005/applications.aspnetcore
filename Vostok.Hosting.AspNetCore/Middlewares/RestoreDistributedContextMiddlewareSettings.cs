﻿using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;

namespace Vostok.Hosting.AspNetCore.Middlewares
{
    /// <summary>
    /// Configuration of the <see cref="TracingMiddleware"/>.
    /// </summary>
    [PublicAPI]
    public class RestoreDistributedContextMiddlewareSettings
    {
        /// <summary>
        /// Additional actions that will be executed during distributed context restoring.
        /// </summary>
        [NotNull]
        public List<Action<HttpRequest>> AdditionalRestoreDistributedContextActions { get; set; } = new List<Action<HttpRequest>>();
    }
}