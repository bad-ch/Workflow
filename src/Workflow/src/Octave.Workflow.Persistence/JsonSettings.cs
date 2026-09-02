using Newtonsoft.Json;
using Workflow.Application.Models;

namespace Workflow.Persistence;

/// <summary>
/// Central configuration for JSON serialization/deserialization across the application.
/// Ensures consistent settings and proper converter registration for WorkflowStep polymorphic deserialization.
/// 
/// IMPORTANT: Since the [JsonConverter] attribute was removed from WorkflowStep to prevent infinite
/// recursion issues with nested deserialization, the converter MUST be registered through this
/// settings object. All JsonSerializer instances used for workflow models must use these settings.
/// </summary>
public static class JsonSettingsProvider
{
    private static readonly Lazy<JsonSerializerSettings> _defaultSettings = new(() => CreateDefaultSettings());

    /// <summary>
    /// Gets the default JSON serializer settings with WorkflowStep converter registered.
    /// Use this for all serialization/deserialization of workflow models.
    /// </summary>
    public static JsonSerializerSettings DefaultSettings => _defaultSettings.Value;

    private static JsonSerializerSettings CreateDefaultSettings()
    {
        var settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "o"
        };

        // Register the WorkflowStep converter manually instead of using [JsonConverter] attribute.
        // This allows us to control the deserialization depth tracking and prevent infinite recursion
        // when deserializing nested WorkflowStep arrays in compound step types like ConditionStep,
        // LoopStep, and ParallelStep.
        settings.Converters.Add(new WorkflowStepConverter());

        return settings;
    }

    /// <summary>
    /// Creates a new JsonSerializer instance with default workflow settings.
    /// </summary>
    public static JsonSerializer CreateSerializer() => JsonSerializer.Create(DefaultSettings);
}

// For backward compatibility with existing code that references "Settings"
public static class JsonSerializerSettingsExtensions
{
    /// <summary>
    /// Legacy static property for backward compatibility.
    /// Use JsonSettingsProvider.DefaultSettings instead for new code.
    /// </summary>
    public static JsonSerializerSettings GetWorkflowSettings() => JsonSettingsProvider.DefaultSettings;
}
