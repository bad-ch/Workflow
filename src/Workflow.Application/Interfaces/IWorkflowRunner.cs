using Newtonsoft.Json.Linq;
using Workflow.Application.Models;
namespace Workflow.Application.Interfaces;
public interface IWorkflowRunner
{
    Task<WorkflowRun> RunAsync(Guid runId, WorkflowDefinition workflow, JObject input, CancellationToken cancellationToken);
}
