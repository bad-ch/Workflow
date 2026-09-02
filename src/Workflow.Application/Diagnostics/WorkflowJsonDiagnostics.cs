using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Workflow.Application.Diagnostics;

/// <summary>
/// Diagnostic utility to inspect workflow JSON files and identify potential deserialization issues.
/// </summary>
public static class WorkflowJsonDiagnostics
{
    /// <summary>
    /// Analyzes a workflow JSON file and reports any potential deserialization issues.
    /// </summary>
    public static DiagnosticReport AnalyzeWorkflowFile(string filePath)
    {
        var report = new DiagnosticReport { FilePath = filePath };

        try
        {
            if (!File.Exists(filePath))
            {
                report.AddError($"File not found: {filePath}");
                return report;
            }

            var json = File.ReadAllText(filePath);
            var workflowJson = JObject.Parse(json);

            // Check for required root properties
            var id = workflowJson["id"];
            var name = workflowJson["name"];
            var steps = workflowJson["steps"];

            if (id == null)
                report.AddWarning("Missing 'id' property at root level");
            if (name == null)
                report.AddWarning("Missing 'name' property at root level");
            if (steps == null)
                report.AddWarning("Missing 'steps' property at root level");

            // Analyze each step
            if (steps is JArray stepsArray)
            {
                for (int i = 0; i < stepsArray.Count; i++)
                {
                    var step = stepsArray[i];
                    AnalyzeStep(step, $"steps[{i}]", report);
                }
            }
        }
        catch (JsonReaderException ex)
        {
            report.AddError($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            report.AddError($"Unexpected error: {ex.Message}");
        }

        return report;
    }

    /// <summary>
    /// Analyzes all workflow JSON files in a directory.
    /// </summary>
    public static List<DiagnosticReport> AnalyzeWorkflowDirectory(string directoryPath)
    {
        var reports = new List<DiagnosticReport>();

        if (!Directory.Exists(directoryPath))
        {
            var errorReport = new DiagnosticReport { FilePath = directoryPath };
            errorReport.AddError($"Directory not found: {directoryPath}");
            reports.Add(errorReport);
            return reports;
        }

        var jsonFiles = Directory.GetFiles(directoryPath, "*.json");
        foreach (var file in jsonFiles)
        {
            reports.Add(AnalyzeWorkflowFile(file));
        }

        return reports;
    }

    private static void AnalyzeStep(JToken? step, string path, DiagnosticReport report)
    {
        if (step == null)
            return;

        if (step is not JObject stepObj)
        {
            report.AddError($"{path}: Step is not a JSON object");
            return;
        }

        var type = stepObj["type"]?.Value<string>();
        if (string.IsNullOrEmpty(type))
        {
            report.AddError($"{path}: Missing 'type' property");
            return;
        }

        var id = stepObj["id"]?.Value<string>();
        if (string.IsNullOrEmpty(id))
        {
            report.AddWarning($"{path} (type: {type}): Missing or empty 'id' property");
        }

        // Type-specific validation
        switch (type.ToLowerInvariant())
        {
            case "httprequest":
            case "http":
                AnalyzeHttpRequestStep(stepObj, path, report);
                break;
            case "condition":
                AnalyzeConditionStep(stepObj, path, report);
                break;
            case "loop":
                AnalyzeLoopStep(stepObj, path, report);
                break;
            case "delay":
                AnalyzeDelayStep(stepObj, path, report);
                break;
            case "script":
                AnalyzeScriptStep(stepObj, path, report);
                break;
            case "parallel":
                AnalyzeParallelStep(stepObj, path, report);
                break;
            default:
                report.AddError($"{path}: Unknown step type '{type}'");
                break;
        }
    }

    private static void AnalyzeHttpRequestStep(JObject step, string path, DiagnosticReport report)
    {
        var method = step["method"] ?? step["Method"];
        if (method == null)
            report.AddError($"{path}: Missing 'method' property");

        var url = step["url"] ?? step["Url"];
        if (url == null)
            report.AddError($"{path}: Missing 'url' property");

        // Check bodyType
        var bodyType = step["bodyType"] ?? step["BodyType"];
        if (bodyType != null)
        {
            ValidateIntegerOrEnumField(bodyType, path, "bodyType", report, 
                "Json", "Xml", "Text", "Form", "Multipart", "Binary");
        }

        // Check timeoutSeconds
        var timeoutSeconds = step["timeoutSeconds"] ?? step["TimeoutSeconds"];
        if (timeoutSeconds != null)
        {
            ValidateIntegerField(timeoutSeconds, path, "timeoutSeconds", report);
        }
    }

    private static void AnalyzeConditionStep(JObject step, string path, DiagnosticReport report)
    {
        var expression = step["expression"] ?? step["Expression"];
        if (expression == null)
            report.AddError($"{path}: Missing 'expression' property");

        var then = step["then"] ?? step["Then"];
        if (then is JArray thenArray)
        {
            for (int i = 0; i < thenArray.Count; i++)
            {
                AnalyzeStep(thenArray[i], $"{path}.then[{i}]", report);
            }
        }

        var elseSteps = step["else"] ?? step["Else"];
        if (elseSteps is JArray elseArray)
        {
            for (int i = 0; i < elseArray.Count; i++)
            {
                AnalyzeStep(elseArray[i], $"{path}.else[{i}]", report);
            }
        }
    }

    private static void AnalyzeLoopStep(JObject step, string path, DiagnosticReport report)
    {
        var itemsExpression = step["itemsExpression"] ?? step["ItemsExpression"];
        if (itemsExpression == null)
            report.AddError($"{path}: Missing 'itemsExpression' property");

        var itemVariable = step["itemVariable"] ?? step["ItemVariable"];
        if (itemVariable == null)
            report.AddError($"{path}: Missing 'itemVariable' property");

        var maxIterations = step["maxIterations"] ?? step["MaxIterations"];
        if (maxIterations != null)
        {
            ValidateIntegerField(maxIterations, path, "maxIterations", report);
        }

        var steps = step["steps"] ?? step["Steps"];
        if (steps is JArray stepsArray)
        {
            for (int i = 0; i < stepsArray.Count; i++)
            {
                AnalyzeStep(stepsArray[i], $"{path}.steps[{i}]", report);
            }
        }
    }

    private static void AnalyzeDelayStep(JObject step, string path, DiagnosticReport report)
    {
        var milliseconds = step["milliseconds"] ?? step["Milliseconds"];
        if (milliseconds == null)
            report.AddError($"{path}: Missing 'milliseconds' property");
        else
            ValidateIntegerField(milliseconds, path, "milliseconds", report);
    }

    private static void AnalyzeScriptStep(JObject step, string path, DiagnosticReport report)
    {
        var assign = step["assign"] ?? step["Assign"];
        if (assign == null)
            report.AddWarning($"{path}: Missing 'assign' property");
        else if (assign is not JObject)
            report.AddError($"{path}: 'assign' should be a JSON object");
    }

    private static void AnalyzeParallelStep(JObject step, string path, DiagnosticReport report)
    {
        var steps = step["steps"] ?? step["Steps"];
        if (steps is JArray stepsArray)
        {
            for (int i = 0; i < stepsArray.Count; i++)
            {
                AnalyzeStep(stepsArray[i], $"{path}.steps[{i}]", report);
            }
        }
        else
        {
            report.AddWarning($"{path}: Missing or invalid 'steps' property");
        }
    }

    private static void ValidateIntegerField(JToken? token, string path, string fieldName, DiagnosticReport report)
    {
        if (token == null)
            return;

        if (token.Type == JTokenType.Integer)
            return;

        if (token.Type == JTokenType.String)
        {
            var str = token.Value<string>();
            if (int.TryParse(str, out _))
                return;

            report.AddWarning($"{path}: Field '{fieldName}' is a string that cannot be parsed as an integer: '{str}'");
            return;
        }

        report.AddError($"{path}: Field '{fieldName}' has unexpected type {token.Type} (expected integer or string)");
    }

    private static void ValidateIntegerOrEnumField(JToken? token, string path, string fieldName, DiagnosticReport report, params string[] validEnumNames)
    {
        if (token == null)
            return;

        if (token.Type == JTokenType.Integer)
            return;

        if (token.Type == JTokenType.String)
        {
            var str = token.Value<string>();

            // Check if it's a valid enum name (case-insensitive)
            if (validEnumNames.Any(name => name.Equals(str, StringComparison.OrdinalIgnoreCase)))
                return;

            // Check if it's a parseable integer
            if (int.TryParse(str, out _))
                return;

            report.AddWarning($"{path}: Field '{fieldName}' has value '{str}' which is neither a valid enum name nor a parseable integer. Valid names are: {string.Join(", ", validEnumNames)}");
            return;
        }

        report.AddError($"{path}: Field '{fieldName}' has unexpected type {token.Type} (expected integer, string, or enum name)");
    }
}

public class DiagnosticReport
{
    public string FilePath { get; set; } = "";
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();

    public bool IsValid => Errors.Count == 0;

    public void AddError(string message) => Errors.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);

    public override string ToString()
    {
        var lines = new List<string> { $"File: {FilePath}" };

        if (Errors.Count > 0)
        {
            lines.Add("Errors:");
            lines.AddRange(Errors.Select(e => $"  ✗ {e}"));
        }

        if (Warnings.Count > 0)
        {
            lines.Add("Warnings:");
            lines.AddRange(Warnings.Select(w => $"  ⚠ {w}"));
        }

        if (Errors.Count == 0 && Warnings.Count == 0)
        {
            lines.Add("✓ No issues found");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
