using Microsoft.Extensions.Options;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;
using Workflow.Persistence.Options;
namespace Workflow.Persistence.Services;
public sealed class JsonRunRepository(IOptions<WorkflowStorageOptions> options) : IRunRepository
{
    private readonly string folder=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,options.Value.RootPath,"results"));
    public Task<WorkflowRun?> GetAsync(Guid id, CancellationToken cancellationToken) => JsonFileStore.ReadAsync<WorkflowRun>(Path.Combine(folder, $"{id:D}.json"), cancellationToken);
    public Task SaveAsync(WorkflowRun run, CancellationToken cancellationToken) => JsonFileStore.WriteAsync(Path.Combine(folder, $"{run.Id:D}.json"), run, cancellationToken);
}
