using Newtonsoft.Json;
using Workflow.Application.Models;

namespace Workflow.Persistence;

/// <summary>
/// Provides configured JSON serializer and settings for consistent serialization
/// across the application, including proper handling of polymorphic WorkflowStep types.
/// </summary>
public static class JsonSettingsProvider
{
    /// <summary>
    /// Default JSON settings with the WorkflowStepConverter registered for polymorphic deserialization.
    /// </summary>
    public static readonly JsonSerializerSettings DefaultSettings = new()
    {
        Converters = new JsonConverter[] { new WorkflowStepConverter() },
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        MaxDepth = 128
    };

    /// <summary>
    /// Creates a JsonSerializer with default settings and the WorkflowStepConverter.
    /// </summary>
    /// <returns>A configured JsonSerializer instance.</returns>
    public static JsonSerializer CreateSerializer()
    {
        return JsonSerializer.Create(DefaultSettings);
    }
}
