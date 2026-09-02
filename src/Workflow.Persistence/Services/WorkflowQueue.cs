using System.Threading.Channels;
using Workflow.Application.Interfaces;

namespace Workflow.Persistence.Services;

#pragma warning disable CA1711
public sealed class WorkflowQueue : IWorkflowQueue
#pragma warning restore CA1711
{
    private readonly Channel<QueuedWorkflow> channel = Channel.CreateBounded<QueuedWorkflow>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ValueTask EnqueueAsync(QueuedWorkflow item, CancellationToken cancellationToken)
        => channel.Writer.WriteAsync(item, cancellationToken);

    public IAsyncEnumerable<QueuedWorkflow> ReadAllAsync(CancellationToken cancellationToken)
        => channel.Reader.ReadAllAsync(cancellationToken);
}
