using Newtonsoft.Json;
namespace Workflow.Persistence.Services;
internal sealed class JsonFileStore
{
    private static readonly JsonSerializerSettings Settings = new() { Formatting = Formatting.Indented, TypeNameHandling = TypeNameHandling.None, Converters = { new Workflow.Application.Models.WorkflowStepConverter() } };
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        using var reader = new StreamReader(stream); using var json = new JsonTextReader(reader);
        return JsonSerializer.Create(Settings).Deserialize<T>(json);
    }
    public static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        await using (var writer = new StreamWriter(stream))
        { using var json = new JsonTextWriter(writer) { Formatting = Formatting.Indented }; JsonSerializer.Create(Settings).Serialize(json, value); await json.FlushAsync(ct); await writer.FlushAsync(ct); }
        File.Move(temp, path, true);
    }
}
