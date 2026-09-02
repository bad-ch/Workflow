using Microsoft.Extensions.Options;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;
using Workflow.Persistence.Options;
namespace Workflow.Persistence.Services;
public sealed class JsonWorkflowRepository(IOptions<WorkflowStorageOptions> options) : IWorkflowRepository, IDisposable
{
    private readonly string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, options.Value.RootPath, "workflows"));
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var list = new List<WorkflowDefinition>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            var item = await JsonFileStore.ReadAsync<WorkflowDefinition>(file, cancellationToken);
            if (item is not null) list.Add(item);
        }
        return list.OrderBy(x => x.Name).ToArray();
    }

    public Task<WorkflowDefinition?> GetAsync(Guid id, CancellationToken cancellationToken)
        => JsonFileStore.ReadAsync<WorkflowDefinition>(PathFor(id), cancellationToken);

    public async Task SaveAsync(WorkflowDefinition workflow, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try { await JsonFileStore.WriteAsync(PathFor(workflow.Id), workflow, cancellationToken); }
        finally { gate.Release(); }
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(PathFor(id)) && Delete(PathFor(id)));

    public void Dispose() => gate.Dispose();

    private static bool Delete(string p) { File.Delete(p); return true; }
    private string PathFor(Guid id) => Path.Combine(folder, $"{id:D}.json");
}
