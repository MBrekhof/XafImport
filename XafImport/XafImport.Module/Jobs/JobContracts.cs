#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XafImport.Module.Jobs
{
    public interface IJobHandler<in TCommand> where TCommand : notnull
    {
        Task ExecuteAsync(TCommand command, CancellationToken ct = default);
    }

    public interface IJobDispatcher
    {
        Task DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : notnull;
        void Schedule<TCommand>(TCommand command, string cronExpression, string jobId) where TCommand : notnull;
        void RemoveSchedule(string jobId);
    }

    // Authenticates the XAF security context inside a background job scope.
    public interface IJobScopeInitializer
    {
        Task InitializeAsync(CancellationToken ct = default);
    }

    // ponytail: inline fallback when Jobs:UseHangfire=false — no queue, no retry, no persistence.
    public sealed class DirectJobDispatcher : IJobDispatcher
    {
        private readonly IServiceProvider services;

        public DirectJobDispatcher(IServiceProvider services) => this.services = services;

        public async Task DispatchAsync<TCommand>(TCommand command, CancellationToken ct = default) where TCommand : notnull
        {
            using var scope = services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IJobHandler<TCommand>>().ExecuteAsync(command, ct);
        }

        public void Schedule<TCommand>(TCommand command, string cronExpression, string jobId) where TCommand : notnull
            => throw new NotSupportedException("Recurring schedules require Jobs:UseHangfire=true.");

        public void RemoveSchedule(string jobId) { }
    }

    public sealed record PingCommand(string Note);

    public sealed class PingHandler : IJobHandler<PingCommand>
    {
        private readonly ILogger<PingHandler> logger;

        public PingHandler(ILogger<PingHandler> logger) => this.logger = logger;

        public Task ExecuteAsync(PingCommand command, CancellationToken ct = default)
        {
            logger.LogInformation("PingCommand executed: {Note}", command.Note);
            return Task.CompletedTask;
        }
    }
}
