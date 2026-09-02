using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Workflow.Application.Models;

public sealed record WorkflowDefinition(
    Guid Id,
    string Name,
    bool Enabled,
    TriggerDefinition Trigger,
    ScheduleDefinition? Schedule,
    IReadOnlyList<WorkflowStep> Steps,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record TriggerDefinition(TriggerType Type, string? WebhookKey = null);
public enum TriggerType { Manual, Webhook, Schedule }
public sealed record ScheduleDefinition(int IntervalSeconds, DateTimeOffset? StartUtc = null);

/// <summary>Controls how <see cref="HttpRequestStep.Body"/> is serialised on the wire.</summary>
public enum BodyType
{
    /// <summary>application/json (default)</summary>
    Json,
    /// <summary>application/xml — Body must be a string containing XML markup</summary>
    Xml,
    /// <summary>text/plain</summary>
    Text,
    /// <summary>application/x-www-form-urlencoded — Body must be a flat JObject of key/value pairs</summary>
    Form,
    /// <summary>multipart/form-data — Body must be a JObject; use "$$file:fieldName" values for binary parts</summary>
    Multipart,
    /// <summary>application/octet-stream — Body must be a Base64-encoded string</summary>
    Binary
}

public abstract record WorkflowStep(string Id, string? Name, bool ContinueOnError = false);
public sealed record HttpRequestStep(string Id, string? Name, string Method, string Url,
    IReadOnlyDictionary<string, string>? Headers = null,
    JToken? Body = null,
    BodyType BodyType = BodyType.Json,
    int TimeoutSeconds = 30,
    bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);
public sealed record ConditionStep(string Id, string? Name, string Expression,
    IReadOnlyList<WorkflowStep> Then, IReadOnlyList<WorkflowStep>? Else = null,
    bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);
public sealed record LoopStep(string Id, string? Name, string ItemsExpression, string ItemVariable,
    IReadOnlyList<WorkflowStep> Steps, int MaxIterations = 100,
    bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);
public sealed record DelayStep(string Id, string? Name, int Milliseconds,
    bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);
public sealed record ScriptStep(string Id, string? Name, IReadOnlyDictionary<string,string> Assign,
    bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);

/// <summary>
/// Runs all child <see cref="Steps"/> concurrently and waits for every branch to complete
/// before the workflow continues. Each branch result is available at
/// <c>steps.{id}.branches.{childId}.output</c>.
/// If <see cref="ContinueOnError"/> is false the step fails as soon as any branch fails.
/// </summary>
public sealed record ParallelStep(string Id, string? Name,
    IReadOnlyList<WorkflowStep> Steps,
    bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);

public sealed class WorkflowStepConverter : JsonConverter<WorkflowStep>
{
    [ThreadStatic]
    private static int _depth;

    /// <summary>Safely parses an integer value from JSON that might be a string or int.</summary>
    private static int? ParseInt(JObject json, string lowerKey, string pascalKey)
    {
        var value = json[lowerKey] ?? json[pascalKey];
        if (value == null) return null;

        if (value.Type == JTokenType.Integer)
            return value.Value<int>();

        if (value.Type == JTokenType.String && int.TryParse(value.Value<string>(), out var result))
            return result;

        return null;
    }

    /// <summary>Safely parses an enum value from JSON that might be a string name or integer value.</summary>
    private static T? ParseEnum<T>(JObject json, string lowerKey, string pascalKey, T? defaultValue = null) where T : struct, Enum
    {
        var value = json[lowerKey] ?? json[pascalKey];
        if (value == null) return defaultValue;

        if (value.Type == JTokenType.Integer)
            return (T)(object)value.Value<int>();

        if (value.Type == JTokenType.String)
        {
            var str = value.Value<string>();
            if (Enum.TryParse<T>(str, ignoreCase: true, out var result))
                return result;
        }

        return defaultValue;
    }

    public override WorkflowStep? ReadJson(JsonReader reader, Type objectType, WorkflowStep? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        _depth++;
        try
        {
            if (_depth > 100)
            {
                throw new JsonSerializationException("WorkflowStep nesting depth exceeded (max 100). Possible circular reference in JSON structure.");
            }

            JObject json;
            try
            {
                json = JObject.Load(reader);
            }
            catch (JsonReaderException ex) when (ex.Message.Contains("MaxDepth"))
            {
                throw new JsonSerializationException("WorkflowStep nesting depth exceeded (max 100). Possible circular reference in JSON structure.", ex);
            }

            if (json == null)
            {
                return null;
            }

            var type = json.Value<string>("type")?.ToLowerInvariant() 
                ?? throw new JsonSerializationException("Step type is required.");

            // For concrete types, we need to manually handle nested arrays
            // because Newtonsoft can't use ToObject with a serializer that lacks the converter
            // (it fails to deserialize WorkflowStep abstract type in nested arrays)
            // and ToObject with a serializer that HAS the converter causes infinite recursion.

            return type switch
            {
                "httprequest" or "http" => DeserializeHttpRequestStep(json, serializer),
                "condition" => DeserializeConditionStep(json, serializer),
                "loop" => DeserializeLoopStep(json, serializer),
                "delay" => DeserializeDelayStep(json, serializer),
                "script" => DeserializeScriptStep(json, serializer),
                "parallel" => DeserializeParallelStep(json, serializer),
                _ => throw new JsonSerializationException($"Unsupported step type '{type}'.")
            };
        }
        finally
        {
            _depth--;
        }
    }

    private static HttpRequestStep DeserializeHttpRequestStep(JObject json, JsonSerializer serializer)
    {
        return new HttpRequestStep(
            Id: json.Value<string>("id") ?? json.Value<string>("Id") ?? "",
            Name: json.Value<string>("name") ?? json.Value<string>("Name"),
            Method: json.Value<string>("method") ?? json.Value<string>("Method") ?? throw new JsonSerializationException("method is required"),
            Url: json.Value<string>("url") ?? json.Value<string>("Url") ?? throw new JsonSerializationException("url is required"),
            Headers: json["headers"]?.ToObject<Dictionary<string, string>>(serializer) ?? json["Headers"]?.ToObject<Dictionary<string, string>>(serializer),
            Body: json["body"] ?? json["Body"],
            BodyType: ParseEnum<BodyType>(json, "bodyType", "BodyType") ?? BodyType.Json,
            TimeoutSeconds: ParseInt(json, "timeoutSeconds", "TimeoutSeconds") ?? 30,
            ContinueOnError: json.Value<bool?>("continueOnError") ?? json.Value<bool?>("ContinueOnError") ?? false
        );
    }

    private ConditionStep DeserializeConditionStep(JObject json, JsonSerializer serializer)
    {
        var then = new List<WorkflowStep>();
        if (json["then"] is JArray thenArray)
        {
            foreach (var item in thenArray)
            {
                using (var itemReader = item.CreateReader())
                {
                    var nestedStep = ReadJson(itemReader, typeof(WorkflowStep), null, false, serializer);
                    if (nestedStep != null)
                        then.Add(nestedStep);
                }
            }
        }
        else if (json["Then"] is JArray thenArray2)
        {
            foreach (var item in thenArray2)
            {
                using (var itemReader = item.CreateReader())
                {
                    var nestedStep = ReadJson(itemReader, typeof(WorkflowStep), null, false, serializer);
                    if (nestedStep != null)
                        then.Add(nestedStep);
                }
            }
        }

        var elseSteps = (IReadOnlyList<WorkflowStep>?)null;
        if (json["else"] is JArray elseArray)
        {
            var elseList = new List<WorkflowStep>();
            foreach (var item in elseArray)
            {
                using (var itemReader = item.CreateReader())
                {
                    var nestedStep = ReadJson(itemReader, typeof(WorkflowStep), null, false, serializer);
                    if (nestedStep != null)
                        elseList.Add(nestedStep);
                }
            }
            elseSteps = elseList;
        }
        else if (json["Else"] is JArray elseArray2)
        {
            var elseList = new List<WorkflowStep>();
            foreach (var item in elseArray2)
            {
                using (var itemReader = item.CreateReader())
                {
                    var nestedStep = ReadJson(itemReader, typeof(WorkflowStep), null, false, serializer);
                    if (nestedStep != null)
                        elseList.Add(nestedStep);
                }
            }
            elseSteps = elseList;
        }

        return new ConditionStep(
            Id: json.Value<string>("id") ?? json.Value<string>("Id") ?? "",
            Name: json.Value<string>("name") ?? json.Value<string>("Name"),
            Expression: json.Value<string>("expression") ?? json.Value<string>("Expression") ?? throw new JsonSerializationException("expression is required"),
            Then: then,
            Else: elseSteps,
            ContinueOnError: json.Value<bool?>("continueOnError") ?? json.Value<bool?>("ContinueOnError") ?? false
        );
    }

    private LoopStep DeserializeLoopStep(JObject json, JsonSerializer serializer)
    {
        var steps = new List<WorkflowStep>();
        JArray? stepsArray = json["steps"] as JArray ?? json["Steps"] as JArray;
        if (stepsArray != null)
        {
            foreach (var item in stepsArray)
            {
                using (var itemReader = item.CreateReader())
                {
                    var nestedStep = ReadJson(itemReader, typeof(WorkflowStep), null, false, serializer);
                    if (nestedStep != null)
                        steps.Add(nestedStep);
                }
            }
        }

        return new LoopStep(
            Id: json.Value<string>("id") ?? json.Value<string>("Id") ?? "",
            Name: json.Value<string>("name") ?? json.Value<string>("Name"),
            ItemsExpression: json.Value<string>("itemsExpression") ?? json.Value<string>("ItemsExpression") ?? throw new JsonSerializationException("itemsExpression is required"),
            ItemVariable: json.Value<string>("itemVariable") ?? json.Value<string>("ItemVariable") ?? throw new JsonSerializationException("itemVariable is required"),
            Steps: steps,
            MaxIterations: ParseInt(json, "maxIterations", "MaxIterations") ?? 100,
            ContinueOnError: json.Value<bool?>("continueOnError") ?? json.Value<bool?>("ContinueOnError") ?? false
        );
    }

    private static DelayStep DeserializeDelayStep(JObject json, JsonSerializer serializer)
    {
        int? milliseconds = ParseInt(json, "milliseconds", "Milliseconds");
        if (!milliseconds.HasValue)
            throw new JsonSerializationException("milliseconds is required");

        return new DelayStep(
            Id: json.Value<string>("id") ?? json.Value<string>("Id") ?? "",
            Name: json.Value<string>("name") ?? json.Value<string>("Name"),
            Milliseconds: milliseconds.Value,
            ContinueOnError: json.Value<bool?>("continueOnError") ?? json.Value<bool?>("ContinueOnError") ?? false
        );
    }

    private static ScriptStep DeserializeScriptStep(JObject json, JsonSerializer serializer)
    {
        return new ScriptStep(
            Id: json.Value<string>("id") ?? json.Value<string>("Id") ?? "",
            Name: json.Value<string>("name") ?? json.Value<string>("Name"),
            Assign: json["assign"]?.ToObject<Dictionary<string, string>>(serializer) ?? json["Assign"]?.ToObject<Dictionary<string, string>>(serializer) ?? new Dictionary<string, string>(),
            ContinueOnError: json.Value<bool?>("continueOnError") ?? json.Value<bool?>("ContinueOnError") ?? false
        );
    }

    private ParallelStep DeserializeParallelStep(JObject json, JsonSerializer serializer)
    {
        var steps = new List<WorkflowStep>();
        JArray? stepsArray = json["steps"] as JArray ?? json["Steps"] as JArray;
        if (stepsArray != null)
        {
            foreach (var item in stepsArray)
            {
                using (var itemReader = item.CreateReader())
                {
                    var nestedStep = ReadJson(itemReader, typeof(WorkflowStep), null, false, serializer);
                    if (nestedStep != null)
                        steps.Add(nestedStep);
                }
            }
        }

        return new ParallelStep(
            Id: json.Value<string>("id") ?? json.Value<string>("Id") ?? "",
            Name: json.Value<string>("name") ?? json.Value<string>("Name"),
            Steps: steps,
            ContinueOnError: json.Value<bool?>("continueOnError") ?? json.Value<bool?>("ContinueOnError") ?? false
        );
    }

    public override void WriteJson(JsonWriter writer, WorkflowStep? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        SerializeWorkflowStep(writer, value, serializer);
    }

    private static void SerializeWorkflowStep(JsonWriter writer, WorkflowStep value, JsonSerializer serializer)
    {
        writer.WriteStartObject();

        // Write the type discriminator first
        writer.WritePropertyName("type");
        writer.WriteValue(value switch
        {
            HttpRequestStep => "http",
            ConditionStep => "condition",
            LoopStep => "loop",
            DelayStep => "delay",
            ScriptStep => "script",
            ParallelStep => "parallel",
            _ => throw new JsonSerializationException($"Unknown step type: {value.GetType().Name}")
        });

        // Now serialize the properties based on type
        switch (value)
        {
            case HttpRequestStep http:
                SerializeHttpRequestStep(writer, http, serializer);
                break;
            case ConditionStep condition:
                SerializeConditionStep(writer, condition, serializer);
                break;
            case LoopStep loop:
                SerializeLoopStep(writer, loop, serializer);
                break;
            case DelayStep delay:
                SerializeDelayStep(writer, delay, serializer);
                break;
            case ScriptStep script:
                SerializeScriptStep(writer, script, serializer);
                break;
            case ParallelStep parallel:
                SerializeParallelStep(writer, parallel, serializer);
                break;
        }

        writer.WriteEndObject();
    }

    private static void SerializeHttpRequestStep(JsonWriter writer, HttpRequestStep step, JsonSerializer serializer)
    {
        writer.WritePropertyName("id");
        writer.WriteValue(step.Id);
        if (step.Name != null)
        {
            writer.WritePropertyName("name");
            writer.WriteValue(step.Name);
        }
        writer.WritePropertyName("method");
        writer.WriteValue(step.Method);
        writer.WritePropertyName("url");
        writer.WriteValue(step.Url);
        if (step.Headers != null && step.Headers.Count > 0)
        {
            writer.WritePropertyName("headers");
            serializer.Serialize(writer, step.Headers);
        }
        if (step.Body != null)
        {
            writer.WritePropertyName("body");
            step.Body.WriteTo(writer);
        }
        if ((int)step.BodyType != 0)
        {
            writer.WritePropertyName("bodyType");
            writer.WriteValue((int)step.BodyType);
        }
        if (step.TimeoutSeconds != 30)
        {
            writer.WritePropertyName("timeoutSeconds");
            writer.WriteValue(step.TimeoutSeconds);
        }
        if (step.ContinueOnError)
        {
            writer.WritePropertyName("continueOnError");
            writer.WriteValue(true);
        }
    }

    private static void SerializeConditionStep(JsonWriter writer, ConditionStep step, JsonSerializer serializer)
    {
        writer.WritePropertyName("id");
        writer.WriteValue(step.Id);
        if (step.Name != null)
        {
            writer.WritePropertyName("name");
            writer.WriteValue(step.Name);
        }
        writer.WritePropertyName("expression");
        writer.WriteValue(step.Expression);

        writer.WritePropertyName("then");
        writer.WriteStartArray();
        foreach (var nestedStep in step.Then)
        {
            SerializeWorkflowStep(writer, nestedStep, serializer);
        }
        writer.WriteEndArray();

        if (step.Else != null && step.Else.Count > 0)
        {
            writer.WritePropertyName("else");
            writer.WriteStartArray();
            foreach (var elseStep in step.Else)
            {
                SerializeWorkflowStep(writer, elseStep, serializer);
            }
            writer.WriteEndArray();
        }

        if (step.ContinueOnError)
        {
            writer.WritePropertyName("continueOnError");
            writer.WriteValue(true);
        }
    }

    private static void SerializeLoopStep(JsonWriter writer, LoopStep step, JsonSerializer serializer)
    {
        writer.WritePropertyName("id");
        writer.WriteValue(step.Id);
        if (step.Name != null)
        {
            writer.WritePropertyName("name");
            writer.WriteValue(step.Name);
        }
        writer.WritePropertyName("itemsExpression");
        writer.WriteValue(step.ItemsExpression);
        writer.WritePropertyName("itemVariable");
        writer.WriteValue(step.ItemVariable);

        writer.WritePropertyName("steps");
        writer.WriteStartArray();
        foreach (var nestedStep in step.Steps)
        {
            SerializeWorkflowStep(writer, nestedStep, serializer);
        }
        writer.WriteEndArray();

        if (step.MaxIterations != 100)
        {
            writer.WritePropertyName("maxIterations");
            writer.WriteValue(step.MaxIterations);
        }
        if (step.ContinueOnError)
        {
            writer.WritePropertyName("continueOnError");
            writer.WriteValue(true);
        }
    }

    private static void SerializeDelayStep(JsonWriter writer, DelayStep step, JsonSerializer serializer)
    {
        writer.WritePropertyName("id");
        writer.WriteValue(step.Id);
        if (step.Name != null)
        {
            writer.WritePropertyName("name");
            writer.WriteValue(step.Name);
        }
        writer.WritePropertyName("milliseconds");
        writer.WriteValue(step.Milliseconds);
        if (step.ContinueOnError)
        {
            writer.WritePropertyName("continueOnError");
            writer.WriteValue(true);
        }
    }

    private static void SerializeScriptStep(JsonWriter writer, ScriptStep step, JsonSerializer serializer)
    {
        writer.WritePropertyName("id");
        writer.WriteValue(step.Id);
        if (step.Name != null)
        {
            writer.WritePropertyName("name");
            writer.WriteValue(step.Name);
        }
        if (step.Assign != null && step.Assign.Count > 0)
        {
            writer.WritePropertyName("assign");
            serializer.Serialize(writer, step.Assign);
        }
        if (step.ContinueOnError)
        {
            writer.WritePropertyName("continueOnError");
            writer.WriteValue(true);
        }
    }

    private static void SerializeParallelStep(JsonWriter writer, ParallelStep step, JsonSerializer serializer)
    {
        writer.WritePropertyName("id");
        writer.WriteValue(step.Id);
        if (step.Name != null)
        {
            writer.WritePropertyName("name");
            writer.WriteValue(step.Name);
        }

        writer.WritePropertyName("steps");
        writer.WriteStartArray();
        foreach (var nestedStep in step.Steps)
        {
            SerializeWorkflowStep(writer, nestedStep, serializer);
        }
        writer.WriteEndArray();

        if (step.ContinueOnError)
        {
            writer.WritePropertyName("continueOnError");
            writer.WriteValue(true);
        }
    }
}

public sealed record WorkflowRun(Guid Id, Guid WorkflowId, RunStatus Status, DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc, JObject Input, JObject Context,
    IReadOnlyList<StepRunResult> Steps, string? ErrorCode = null, string? Error = null);
public sealed record StepRunResult(string StepId, RunStatus Status, DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc, JToken? Output = null, string? ErrorCode = null, string? Error = null);
public enum RunStatus { Running, Succeeded, Failed, Skipped }

public sealed record CreateWorkflowRequest(string Name, bool Enabled, TriggerDefinition Trigger,
    ScheduleDefinition? Schedule, IReadOnlyList<WorkflowStep> Steps);
public sealed record UpdateWorkflowRequest(string Name, bool Enabled, TriggerDefinition Trigger,
    ScheduleDefinition? Schedule, IReadOnlyList<WorkflowStep> Steps);
public sealed record RunWorkflowRequest(JObject? Input);
public sealed record WorkflowResponse(Guid Id, string Name, bool Enabled, TriggerDefinition Trigger,
    ScheduleDefinition? Schedule, IReadOnlyList<WorkflowStep> Steps, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);
