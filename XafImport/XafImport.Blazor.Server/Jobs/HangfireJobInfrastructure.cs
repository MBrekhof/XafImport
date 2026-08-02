#nullable enable
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using Hangfire;
using Hangfire.Dashboard;
using XafImport.Module.BusinessObjects;
using XafImport.Module.Jobs;

namespace XafImport.Blazor.Server.Jobs
{
    public sealed class HangfireJobDispatcher : IJobDispatcher
    {
        public Task DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : notnull
        {
            BackgroundJob.Enqueue<JobExecutor<TCommand>>(x => x.RunAsync(command, CancellationToken.None));
            return Task.CompletedTask;
        }

        public void Schedule<TCommand>(TCommand command, string cronExpression, string jobId) where TCommand : notnull
            => RecurringJob.AddOrUpdate<JobExecutor<TCommand>>(jobId, x => x.RunAsync(command, CancellationToken.None), cronExpression);

        public void RemoveSchedule(string jobId) => RecurringJob.RemoveIfExists(jobId);
    }

    // What Hangfire actually invokes: authenticates the XAF scope, then runs the handler.
    public class JobExecutor<TCommand> where TCommand : notnull
    {
        private readonly IJobHandler<TCommand> handler;
        private readonly IJobScopeInitializer scopeInitializer;

        public JobExecutor(IJobHandler<TCommand> handler, IJobScopeInitializer scopeInitializer)
        {
            this.handler = handler;
            this.scopeInitializer = scopeInitializer;
        }

        [AutomaticRetry(Attempts = 3)]
        public async Task RunAsync(TCommand command, CancellationToken ct)
        {
            await scopeInitializer.InitializeAsync(ct);
            await handler.ExecuteAsync(command, ct);
        }
    }

    // Hangfire workers have no HTTP context/circuit: log on as the HangfireJob service user.
    public sealed class XafJobScopeInitializer : IJobScopeInitializer
    {
        private const string ServiceUserName = "HangfireJob";
        private readonly IServiceProvider serviceProvider;
        private readonly IConfiguration configuration;

        public XafJobScopeInitializer(IServiceProvider serviceProvider, IConfiguration configuration)
        {
            this.serviceProvider = serviceProvider;
            this.configuration = configuration;
        }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            var security = serviceProvider.GetRequiredService<ISecurityStrategyBase>();
            if (security.IsAuthenticated)
            {
                return Task.CompletedTask;
            }
            var password = configuration["HangfireJob:Password"] ?? string.Empty;
            var strategy = (SecurityStrategy)security;
            strategy.Authentication.SetLogonParameters(new AuthenticationStandardLogonParameters(ServiceUserName, password));
            var nonSecuredFactory = serviceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
            using var logonSpace = nonSecuredFactory.CreateNonSecuredObjectSpace<ApplicationUser>();
            strategy.Logon(logonSpace);
            return Task.CompletedTask;
        }
    }

    // ponytail: dashboard is dev-only; wire an XAF role check here before any real deployment.
    public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
    {
        private readonly bool isDevelopment;

        public HangfireDashboardAuthFilter(bool isDevelopment) => this.isDevelopment = isDevelopment;

        public bool Authorize(DashboardContext context) => isDevelopment;
    }

    public static class JobServiceCollectionExtensions
    {
        public static IServiceCollection AddJobDispatcher(this IServiceCollection services, IConfiguration configuration)
        {
            if (configuration.GetValue<bool>("Jobs:UseHangfire"))
            {
                var connectionString = configuration.GetConnectionString("ConnectionString");
                services.AddHangfire(h => h
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSqlServerStorage(connectionString));
                // ponytail: worker runs in Development too — single-machine POC, no shared dev DB to protect.
                services.AddHangfireServer();
                services.AddScoped<IJobDispatcher, HangfireJobDispatcher>();
            }
            else
            {
                services.AddScoped<IJobDispatcher, DirectJobDispatcher>();
            }
            services.AddScoped<IJobScopeInitializer, XafJobScopeInitializer>();
            return services;
        }

        public static IServiceCollection AddJobHandler<TCommand, THandler>(this IServiceCollection services)
            where TCommand : notnull
            where THandler : class, IJobHandler<TCommand>
        {
            services.AddScoped<IJobHandler<TCommand>, THandler>();
            services.AddScoped<JobExecutor<TCommand>>();
            return services;
        }
    }
}
