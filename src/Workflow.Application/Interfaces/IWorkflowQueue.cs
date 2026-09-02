using Newtonsoft.Json.Linq;
namespace Workflow.Application.Interfaces;
public sealed record QueuedWorkflow(Guid RunId, Guid WorkflowId, JObject Input);
#pragma warning disable CA1711
public interface IWorkflowQueue
#pragma warning restore CA1711
{
    ValueTask EnqueueAsync(QueuedWorkflow item, CancellationToken cancellationToken);
    IAsyncEnumerable<QueuedWorkflow> ReadAllAsync(CancellationToken cancellationToken);
}
