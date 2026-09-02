using Workflow.Application.Models;
namespace Workflow.Application.Interfaces;
public interface IWorkflowRepository
{
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken cancellationToken);
    Task<WorkflowDefinition?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(WorkflowDefinition workflow, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
