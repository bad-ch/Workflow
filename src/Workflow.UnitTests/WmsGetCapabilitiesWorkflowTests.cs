using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Workflow.Application.Models;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Unit tests for the WMS GetCapabilities workflow (cccccccc-0000-0000-0000-00000000000c).
/// This workflow fetches WMS (Web Map Service) metadata and extracts layer information.
/// 
/// Workflow Steps:
/// 1. get-capabilities: HTTP GET request to WMS endpoint (returns XML)
/// 2. extract-service-info: Script step that extracts metadata using JSONPath expressions
/// </summary>
public class WmsGetCapabilitiesWorkflowTests
{
    private static readonly Guid WmsWorkflowId = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly string WmsWorkflowName = "WMS — List Available Layers";
    private static readonly string WmsEndpoint = "https://ms.web-gis.ch/api/Ogc/e5a071f2-ffce-4d18-9f67-dfd8052cd7c9?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities";

    private static JsonSerializerSettings GetWorkflowSerializerSettings()
    {
        var settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new JsonConverter[] { new WorkflowStepConverter() }
        };
        return settings;
    }

    /// <summary>
    /// Verifies that the WMS workflow can be loaded from disk and has the expected structure.
    /// </summary>
    [Fact]
    public void WmsWorkflowCanBeLoadedFromDisk()
    {
        // Arrange
        var workflowPath = Path.Combine(
            Path.GetDirectoryName(typeof(WmsGetCapabilitiesWorkflowTests).Assembly.Location) ?? "",
            "..", "..", "..", "..",
            "Workflow.Api", "data", "workflows",
            "cccccccc-0000-0000-0000-00000000000c.json"
        );

        // Act
        Assert.True(File.Exists(workflowPath), $"Workflow file not found at: {workflowPath}");
        var json = File.ReadAllText(workflowPath);
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.Equal(WmsWorkflowId, workflow.Id);
        Assert.Equal(WmsWorkflowName, workflow.Name);
        Assert.True(workflow.Enabled);
    }

    /// <summary>
    /// Verifies the WMS workflow has exactly 2 steps: HTTP request and script extraction.
    /// </summary>
    [Fact]
    public void WmsWorkflowHasCorrectStepStructure()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();

        // Act
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);
        Assert.Equal(2, workflow.Steps.Count);

        // Step 1: HTTP request
        var httpStep = Assert.IsType<HttpRequestStep>(workflow.Steps[0]);
        Assert.Equal("get-capabilities", httpStep.Id);
        Assert.Equal("GET WMS GetCapabilities (XML)", httpStep.Name);

        // Step 2: Script extraction
        var scriptStep = Assert.IsType<ScriptStep>(workflow.Steps[1]);
        Assert.Equal("extract-service-info", scriptStep.Id);
        Assert.Equal("Extract service metadata", scriptStep.Name);
    }

    /// <summary>
    /// Verifies the first step (HTTP request) has correct configuration.
    /// </summary>
    [Fact]
    public void WmsHttpStepHasCorrectConfiguration()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);
        var httpStep = Assert.IsType<HttpRequestStep>(workflow.Steps[0]);

        // Act & Assert
        Assert.Equal("get-capabilities", httpStep.Id);
        Assert.Equal("GET WMS GetCapabilities (XML)", httpStep.Name);
        Assert.Equal("GET", httpStep.Method);
        Assert.Equal(WmsEndpoint, httpStep.Url);
        Assert.Equal(30, httpStep.TimeoutSeconds);
        Assert.False(httpStep.ContinueOnError);

        // Verify headers
        Assert.NotNull(httpStep.Headers);
        Assert.Single(httpStep.Headers);
        Assert.True(httpStep.Headers.ContainsKey("Accept"));
        Assert.Equal("text/xml", httpStep.Headers["Accept"]);

        // No request body for GET
        Assert.Null(httpStep.Body);
    }

    /// <summary>
    /// Verifies the second step (script extraction) extracts the expected metadata fields.
    /// </summary>
    [Fact]
    public void WmsScriptStepExtractsAllMetadataFields()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);
        var scriptStep = Assert.IsType<ScriptStep>(workflow.Steps[1]);

        // Act & Assert
        Assert.Equal("extract-service-info", scriptStep.Id);
        Assert.Equal("Extract service metadata", scriptStep.Name);
        Assert.False(scriptStep.ContinueOnError);

        // Verify all assigned metadata fields
        var expectedAssignments = new[]
        {
            "serviceTitle",
            "wmsVersion",
            "maxWidth",
            "maxHeight",
            "onlineResource",
            "rootLayerTitle",
            "layerName",
            "layerTitle",
            "layerCrs"
        };

        Assert.Equal(9, scriptStep.Assign.Count);
        foreach (var key in expectedAssignments)
        {
            Assert.True(scriptStep.Assign.ContainsKey(key), $"Missing assignment: {key}");
            Assert.NotEmpty(scriptStep.Assign[key]);
        }
    }

    /// <summary>
    /// Verifies the script step uses correct JSONPath expressions to extract metadata.
    /// </summary>
    [Fact]
    public void WmsScriptStepUsesCorrectJsonPathExpressions()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);
        var scriptStep = Assert.IsType<ScriptStep>(workflow.Steps[1]);

        // Act & Assert
        // Verify expressions reference the HTTP step's output
        var serviceTitle = scriptStep.Assign["serviceTitle"];
        Assert.Contains("{{steps.get-capabilities.output.body.WMS_Capabilities.Service.Title}}", serviceTitle);

        var wmsVersion = scriptStep.Assign["wmsVersion"];
        Assert.Contains("{{steps.get-capabilities.output.body.WMS_Capabilities.@version}}", wmsVersion);

        var maxWidth = scriptStep.Assign["maxWidth"];
        Assert.Contains("{{steps.get-capabilities.output.body.WMS_Capabilities.Service.MaxWidth}}", maxWidth);

        var layerName = scriptStep.Assign["layerName"];
        Assert.Contains("{{steps.get-capabilities.output.body.WMS_Capabilities.Capability.Layer.Layer.Name}}", layerName);

        var layerCrs = scriptStep.Assign["layerCrs"];
        Assert.Contains("{{steps.get-capabilities.output.body.WMS_Capabilities.Capability.Layer.Layer.CRS}}", layerCrs);
    }

    /// <summary>
    /// Verifies the workflow has correct trigger configuration (Manual).
    /// </summary>
    [Fact]
    public void WmsWorkflowHasManualTrigger()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();

        // Act
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.NotNull(workflow.Trigger);
        Assert.Equal(TriggerType.Manual, workflow.Trigger.Type);
        Assert.Null(workflow.Trigger.WebhookKey);
        Assert.Null(workflow.Schedule);
    }

    /// <summary>
    /// Verifies the workflow can be serialized back to JSON and round-tripped.
    /// </summary>
    [Fact]
    public void WmsWorkflowCanBeRoundTrippedThroughSerialization()
    {
        // Arrange
        var originalJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(originalJson, settings);
        Assert.NotNull(workflow);

        // Act
        var serializedJson = JsonConvert.SerializeObject(workflow, settings);
        var deserializedWorkflow = JsonConvert.DeserializeObject<WorkflowDefinition>(serializedJson, settings);

        // Assert
        Assert.NotNull(deserializedWorkflow);
        Assert.Equal(workflow.Id, deserializedWorkflow.Id);
        Assert.Equal(workflow.Name, deserializedWorkflow.Name);
        Assert.Equal(workflow.Enabled, deserializedWorkflow.Enabled);
        Assert.Equal(workflow.Steps.Count, deserializedWorkflow.Steps.Count);

        var originalHttpStep = Assert.IsType<HttpRequestStep>(workflow.Steps[0]);
        var deserializedHttpStep = Assert.IsType<HttpRequestStep>(deserializedWorkflow.Steps[0]);
        Assert.Equal(originalHttpStep.Method, deserializedHttpStep.Method);
        Assert.Equal(originalHttpStep.Url, deserializedHttpStep.Url);
    }

    /// <summary>
    /// Verifies the workflow step IDs are unique within the workflow.
    /// </summary>
    [Fact]
    public void WmsWorkflowStepIdsAreUnique()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);

        // Act
        var stepIds = workflow.Steps.Select(s => s.Id).ToList();
        var uniqueIds = new HashSet<string>(stepIds);

        // Assert
        Assert.Equal(stepIds.Count, uniqueIds.Count);
        Assert.All(stepIds, id => Assert.NotEmpty(id));
    }

    /// <summary>
    /// Verifies that deserialization handles the mixed-case property names (both "Id" and "id").
    /// </summary>
    [Fact]
    public void WmsWorkflowDeserializesWithMixedCaseProperties()
    {
        // Arrange
        var mixedCaseJson = @"{
            ""Id"": ""cccccccc-0000-0000-0000-00000000000c"",
            ""Name"": ""WMS — List Available Layers"",
            ""Enabled"": true,
            ""Trigger"": { ""Type"": ""Manual"" },
            ""Schedule"": null,
            ""Steps"": [
                {
                    ""type"": ""http"",
                    ""id"": ""get-capabilities"",
                    ""name"": ""GET WMS GetCapabilities (XML)"",
                    ""method"": ""GET"",
                    ""url"": ""https://ms.web-gis.ch/api/Ogc/..?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities"",
                    ""headers"": { ""Accept"": ""text/xml"" },
                    ""timeoutSeconds"": 30,
                    ""continueOnError"": false
                }
            ],
            ""createdUtc"": ""2025-04-01T00:00:00+00:00"",
            ""updatedUtc"": ""2025-04-01T00:00:00+00:00""
        }";

        var settings = GetWorkflowSerializerSettings();

        // Act
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(mixedCaseJson, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.Equal(WmsWorkflowId, workflow.Id);
        Assert.Equal(WmsWorkflowName, workflow.Name);
    }

    /// <summary>
    /// Verifies the workflow metadata (timestamps, enabled status) is correctly structured.
    /// </summary>
    [Fact]
    public void WmsWorkflowMetadataIsCorrectlyStructured()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();

        // Act
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.Equal(WmsWorkflowId, workflow.Id);
        Assert.Equal(WmsWorkflowName, workflow.Name);
        Assert.True(workflow.Enabled, "Workflow should be enabled");
        Assert.NotNull(workflow.Trigger);
        Assert.True(workflow.UpdatedUtc >= workflow.CreatedUtc, "UpdatedUtc should be >= CreatedUtc");
    }

    /// <summary>
    /// Verifies HTTP step accepts XML response type (Content-Type: text/xml).
    /// </summary>
    [Fact]
    public void WmsHttpStepIsConfiguredForXmlResponse()
    {
        // Arrange
        var workflowJson = GetWmsWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);
        var httpStep = Assert.IsType<HttpRequestStep>(workflow.Steps[0]);

        // Act & Assert
        Assert.NotNull(httpStep.Headers);

        // The Accept header indicates XML response is expected
        Assert.True(httpStep.Headers.ContainsKey("Accept"));
        Assert.Equal("text/xml", httpStep.Headers["Accept"]);

        // No specific body type set means JSON by default (but response will be auto-converted from XML)
        Assert.Equal(BodyType.Json, httpStep.BodyType);
    }

    // ==================== Helper Methods ====================

    /// <summary>
    /// Returns the WMS workflow JSON for testing.
    /// This is embedded here to ensure tests are independent and can run offline.
    /// </summary>
    private static string GetWmsWorkflowJson()
    {
        return @"{
  ""Id"": ""cccccccc-0000-0000-0000-00000000000c"",
  ""Name"": ""WMS — List Available Layers"",
  ""Enabled"": true,
  ""Trigger"": { ""Type"": ""Manual"" },
  ""Schedule"": null,
  ""Steps"": [
    {
      ""type"": ""http"",
      ""Id"": ""get-capabilities"",
      ""Name"": ""GET WMS GetCapabilities (XML)"",
      ""Method"": ""GET"",
      ""Url"": ""https://ms.web-gis.ch/api/Ogc/e5a071f2-ffce-4d18-9f67-dfd8052cd7c9?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetCapabilities"",
      ""Headers"": {
        ""Accept"": ""text/xml""
      },
      ""TimeoutSeconds"": 30,
      ""ContinueOnError"": false
    },
    {
      ""type"": ""script"",
      ""Id"": ""extract-service-info"",
      ""Name"": ""Extract service metadata"",
      ""Assign"": {
        ""serviceTitle"":   ""{{steps.get-capabilities.output.body.WMS_Capabilities.Service.Title}}"",
        ""wmsVersion"":     ""{{steps.get-capabilities.output.body.WMS_Capabilities.@version}}"",
        ""maxWidth"":       ""{{steps.get-capabilities.output.body.WMS_Capabilities.Service.MaxWidth}}"",
        ""maxHeight"":      ""{{steps.get-capabilities.output.body.WMS_Capabilities.Service.MaxHeight}}"",
        ""onlineResource"": ""{{steps.get-capabilities.output.body.WMS_Capabilities.Service.OnlineResource.@xlink:href}}"",
        ""rootLayerTitle"": ""{{steps.get-capabilities.output.body.WMS_Capabilities.Capability.Layer.Title}}"",
        ""layerName"":      ""{{steps.get-capabilities.output.body.WMS_Capabilities.Capability.Layer.Layer.Name}}"",
        ""layerTitle"":     ""{{steps.get-capabilities.output.body.WMS_Capabilities.Capability.Layer.Layer.Title}}"",
        ""layerCrs"":       ""{{steps.get-capabilities.output.body.WMS_Capabilities.Capability.Layer.Layer.CRS}}""
      },
      ""ContinueOnError"": false
    }
  ],
  ""CreatedUtc"": ""2025-04-01T00:00:00+00:00"",
  ""UpdatedUtc"": ""2025-04-01T00:00:00+00:00""
}";
    }
}
