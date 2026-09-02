using Workflow.Application.Models;
namespace Workflow.Application.Interfaces;
public interface IRunRepository
{
    Task<WorkflowRun?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(WorkflowRun run, CancellationToken cancellationToken);
}
