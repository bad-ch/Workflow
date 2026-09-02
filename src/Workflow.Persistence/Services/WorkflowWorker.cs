using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workflow.Application.Interfaces;

namespace Workflow.Persistence.Services;

public sealed class WorkflowWorker(
    IWorkflowQueue queue,
    IWorkflowRepository workflows,
    IWorkflowRunner runner,
    ILogger<WorkflowWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, Guid, Exception> LogWorkflowFailed =
        LoggerMessage.Define<Guid>(LogLevel.Error, default, "Queued workflow {WorkflowId} failed");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                var workflow = await workflows.GetAsync(item.WorkflowId, stoppingToken);
                if (workflow is null || !workflow.Enabled) continue;
                await runner.RunAsync(item.RunId, workflow, item.Input, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                LogWorkflowFailed(logger, item.WorkflowId, ex);
            }
        }
    }
}
