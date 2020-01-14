﻿using System;
using JetBrains.Annotations;
using Vostok.Throttling;
using Vostok.Throttling.Quotas;

namespace Vostok.Applications.AspNetCore.Builders
{
    [PublicAPI]
    public static class IVostokThrottlingBuilderExtensions
    {
        /// <summary>
        /// Disables the throttling middleware altogether.
        /// </summary>
        [NotNull]
        public static IVostokThrottlingBuilder DisableThrottling([NotNull] this IVostokThrottlingBuilder builder)
            => builder.CustomizeSettings(s => s.Enabled = _ => false);

        /// <summary>
        /// Disables generation of throttling metrics.
        /// </summary>
        [NotNull]
        public static IVostokThrottlingBuilder DisableMetrics([NotNull] this IVostokThrottlingBuilder builder)
            => builder.CustomizeSettings(s => s.Metrics = null);

        /// <summary>
        /// <para>Sets up a quota on the <see cref="WellKnownThrottlingProperties.Consumer"/> request property configured by given <paramref name="quotaOptionsProvider"/>.</para>
        /// <para>See <see cref="IVostokThrottlingBuilder.UsePropertyQuota"/> for additional info on property quotas.</para>
        /// </summary>
        [NotNull]
        public static IVostokThrottlingBuilder UseConsumerQuota([NotNull] this IVostokThrottlingBuilder builder, [NotNull] Func<PropertyQuotaOptions> quotaOptionsProvider) =>
            builder
                .CustomizeSettings(settings => settings.AddConsumerProperty = true)
                .UsePropertyQuota(WellKnownThrottlingProperties.Consumer, quotaOptionsProvider);

        /// <summary>
        /// <para>Sets up a quota on the <see cref="WellKnownThrottlingProperties.Priority"/> request property configured by given <paramref name="quotaOptionsProvider"/>.</para>
        /// <para>See <see cref="IVostokThrottlingBuilder.UsePropertyQuota"/> for additional info on property quotas.</para>
        /// </summary>
        [NotNull]
        public static IVostokThrottlingBuilder UsePriorityQuota([NotNull] this IVostokThrottlingBuilder builder, [NotNull] Func<PropertyQuotaOptions> quotaOptionsProvider) => 
            builder
                .CustomizeSettings(settings => settings.AddPriorityProperty = true)
                .UsePropertyQuota(WellKnownThrottlingProperties.Priority, quotaOptionsProvider);

        /// <summary>
        /// <para>Sets up a quota on the <see cref="WellKnownThrottlingProperties.Method"/> request property configured by given <paramref name="quotaOptionsProvider"/>.</para>
        /// <para>See <see cref="IVostokThrottlingBuilder.UsePropertyQuota"/> for additional info on property quotas.</para>
        /// </summary>
        [NotNull]
        public static IVostokThrottlingBuilder UseMethodQuota([NotNull] this IVostokThrottlingBuilder builder, [NotNull] Func<PropertyQuotaOptions> quotaOptionsProvider) => 
            builder
                .CustomizeSettings(settings => settings.AddMethodProperty = true)
                .UsePropertyQuota(WellKnownThrottlingProperties.Method, quotaOptionsProvider);

        /// <summary>
        /// <para>Sets up a quota on the <see cref="WellKnownThrottlingProperties.Url"/> request property configured by given <paramref name="quotaOptionsProvider"/>.</para>
        /// <para>See <see cref="IVostokThrottlingBuilder.UsePropertyQuota"/> for additional info on property quotas.</para>
        /// </summary>
        [NotNull]
        public static IVostokThrottlingBuilder UseUrlQuota([NotNull] this IVostokThrottlingBuilder builder, [NotNull] Func<PropertyQuotaOptions> quotaOptionsProvider) => 
            builder
                .CustomizeSettings(settings => settings.AddUrlProperty = true)
                .UsePropertyQuota(WellKnownThrottlingProperties.Url, quotaOptionsProvider);
    }
}
