using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Workflow.Application.Models;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Tests to validate that all workflow JSON files in the data folder can be deserialized successfully.
/// This helps catch any JSON format issues early.
/// </summary>
public class WorkflowJsonValidationTests
{
    private static readonly string WorkflowsDataPath = Path.Combine(
        Path.GetDirectoryName(typeof(WorkflowJsonValidationTests).Assembly.Location) ?? "",
        "..", "..", "..", "..",
        "Workflow.Api", "data", "workflows"
    );

    private static JsonSerializerSettings GetWorkflowSerializerSettings()
    {
        var settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new JsonConverter[] { new WorkflowStepConverter() }
        };
        return settings;
    }

    [Fact]
    public void AllWorkflowJsonFilesCanBeDeserialized()
    {
        // Ensure the data directory exists
        if (!Directory.Exists(WorkflowsDataPath))
        {
            Assert.Fail($"Workflows data path not found: {WorkflowsDataPath}");
        }

        var jsonFiles = Directory.GetFiles(WorkflowsDataPath, "*.json");

        // Should have at least some workflow files
        Assert.NotEmpty(jsonFiles);

        var settings = GetWorkflowSerializerSettings();
        var failedFiles = new List<(string FilePath, Exception Error)>();

        foreach (var filePath in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

                // Validate that critical properties are populated
                Assert.NotNull(workflow);
                Assert.NotNull(workflow.Name);
                Assert.NotEmpty(workflow.Steps);
            }
            catch (Exception ex)
            {
                failedFiles.Add((filePath, ex));
            }
        }

        // Report any failures
        if (failedFiles.Count > 0)
        {
            var errorMessage = "The following workflow files failed to deserialize:\n" +
                string.Join("\n", failedFiles.Select(f => 
                    $"  - {Path.GetFileName(f.FilePath)}: {f.Error.GetType().Name} - {f.Error.Message}"));

            Assert.Fail(errorMessage);
        }
    }

    [Fact]
    public void HttpRequestStepDeserializesWithIntegerBodyType()
    {
        var json = @"{
            ""type"": ""http"",
            ""id"": ""step1"",
            ""method"": ""GET"",
            ""url"": ""http://example.com"",
            ""bodyType"": 0
        }";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<HttpRequestStep>(step);

        var httpStep = (HttpRequestStep)step;
        Assert.Equal(BodyType.Json, httpStep.BodyType);
    }

    [Fact]
    public void HttpRequestStepDeserializesWithStringBodyType()
    {
        var json = @"{
            ""type"": ""http"",
            ""id"": ""step1"",
            ""method"": ""GET"",
            ""url"": ""http://example.com"",
            ""bodyType"": ""Json""
        }";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<HttpRequestStep>(step);

        var httpStep = (HttpRequestStep)step;
        Assert.Equal(BodyType.Json, httpStep.BodyType);
    }

    [Fact]
    public void HttpRequestStepDeserializesWithStringBodyTypeVariants()
    {
        var bodyTypes = new[] { "Json", "json", "JSON", "Xml", "xml", "Text", "text", "Form", "form", "Multipart", "multipart", "Binary", "binary" };
        var settings = GetWorkflowSerializerSettings();

        foreach (var bodyType in bodyTypes)
        {
            var json = @"{
                ""type"": ""http"",
                ""id"": ""step1"",
                ""method"": ""GET"",
                ""url"": ""http://example.com"",
                ""bodyType"": """ + bodyType + @"""
            }";

            try
            {
                var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);
                Assert.NotNull(step);
                Assert.IsType<HttpRequestStep>(step);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to deserialize bodyType '{bodyType}': {ex.Message}");
            }
        }
    }

    [Fact]
    public void HttpRequestStepDeserializesWithStringTimeoutSeconds()
    {
        var json = @"{
            ""type"": ""http"",
            ""id"": ""step1"",
            ""method"": ""GET"",
            ""url"": ""http://example.com"",
            ""timeoutSeconds"": ""60""
        }";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<HttpRequestStep>(step);

        var httpStep = (HttpRequestStep)step;
        Assert.Equal(60, httpStep.TimeoutSeconds);
    }

    [Fact]
    public void DelayStepDeserializesWithStringMilliseconds()
    {
        var json = @"{
            ""type"": ""delay"",
            ""id"": ""step1"",
            ""milliseconds"": ""5000""
        }";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<DelayStep>(step);

        var delayStep = (DelayStep)step;
        Assert.Equal(5000, delayStep.Milliseconds);
    }

    [Fact]
    public void LoopStepDeserializesWithStringMaxIterations()
    {
        var json = @"{
            ""type"": ""loop"",
            ""id"": ""step1"",
            ""itemsExpression"": ""items"",
            ""itemVariable"": ""item"",
            ""maxIterations"": ""200"",
            ""steps"": []
        }";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<LoopStep>(step);

        var loopStep = (LoopStep)step;
        Assert.Equal(200, loopStep.MaxIterations);
    }

    [Fact]
    public void HttpRequestStepHandlesInvalidStringNumberGracefully()
    {
        var json = @"{
            ""type"": ""http"",
            ""id"": ""step1"",
            ""method"": ""GET"",
            ""url"": ""http://example.com"",
            ""timeoutSeconds"": ""invalid""
        }";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<HttpRequestStep>(step);

        // Should fall back to default value of 30 when parsing fails
        var httpStep = (HttpRequestStep)step;
        Assert.Equal(30, httpStep.TimeoutSeconds);
    }

    [Theory]
    [InlineData("bodyType", 0)]
    [InlineData("bodyType", "Json")]
    [InlineData("BodyType", 1)]
    [InlineData("BodyType", "Xml")]
    public void HttpRequestStepHandlesCaseVariations(string propertyName, object value)
    {
        var json = $@"{{
            ""type"": ""http"",
            ""id"": ""step1"",
            ""method"": ""GET"",
            ""url"": ""http://example.com"",
            ""{propertyName}"": {(value is string ? $"\"{value}\"" : value)}
        }}";

        var settings = GetWorkflowSerializerSettings();
        var step = JsonConvert.DeserializeObject<WorkflowStep>(json, settings);

        Assert.NotNull(step);
        Assert.IsType<HttpRequestStep>(step);
    }
}
