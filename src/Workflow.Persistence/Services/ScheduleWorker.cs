using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;

namespace Workflow.Persistence.Services;

public sealed class ScheduleWorker(
    IWorkflowRepository workflows,
    IWorkflowQueue queue,
    ILogger<ScheduleWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Guid, Exception?> LogScheduled =
        LoggerMessage.Define<Guid>(LogLevel.Information, default, "Scheduled workflow {WorkflowId}");

    private readonly Dictionary<Guid, DateTimeOffset> nextRuns = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var w in await workflows.GetAllAsync(stoppingToken))
            {
                if (!w.Enabled || w.Trigger.Type != TriggerType.Schedule || w.Schedule is null) continue;
                var next = nextRuns.GetValueOrDefault(w.Id, w.Schedule.StartUtc ?? now);
                if (next > now) continue;
                await queue.EnqueueAsync(new(Guid.NewGuid(), w.Id, new JObject { { "trigger", "schedule" }, { "scheduledUtc", now } }), stoppingToken);
                nextRuns[w.Id] = now.AddSeconds(w.Schedule.IntervalSeconds);
                LogScheduled(logger, w.Id, null);
            }
        }
    }
}
