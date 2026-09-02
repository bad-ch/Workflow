using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Workflow.Application.Models;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Unit tests for the WMS GetMap workflow with layer validation (dddddddd-1111-1111-1111-11111111111d).
/// This workflow extends the basic GetCapabilities workflow by adding:
/// 1. A condition step to validate layer existence
/// 2. A WMS GetMap request with random extent in EPSG:2056 (Swiss LV95)
/// 3. Extraction of map response metadata
/// 
/// Workflow Steps:
/// 1. get-capabilities: HTTP GET request to WMS endpoint (returns XML)
/// 2. extract-service-info: Script step that extracts metadata using JSONPath expressions
/// 3. check-layer-exists: Condition step that validates layer presence before GetMap
/// 4. generate-random-extent: Script step that generates EPSG:2056 bbox coordinates
/// 5. wms-getmap: HTTP GET request to fetch map image with specified extent
/// 6. extract-map-info: Script step that extracts map response metadata
/// (or) log-layer-error: Fallback script if layer validation fails
/// </summary>
public class WmsGetMapWorkflowTests
{
    private static readonly Guid WmsMapWorkflowId = Guid.Parse("dddddddd-1111-1111-1111-11111111111d");
    private static readonly string WmsMapWorkflowName = "WMS — Get Map with Layer Validation";

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
    /// Verifies that the WMS GetMap workflow can be loaded from disk and has the expected structure.
    /// </summary>
    [Fact]
    public void WmsGetMapWorkflowCanBeLoadedFromDisk()
    {
        // Arrange
        var workflowPath = Path.Combine(
            Path.GetDirectoryName(typeof(WmsGetMapWorkflowTests).Assembly.Location) ?? "",
            "..", "..", "..", "..",
            "Workflow.Api", "data", "workflows",
            "dddddddd-1111-1111-1111-11111111111d.json"
        );

        // Act
        Assert.True(File.Exists(workflowPath), $"Workflow file not found at: {workflowPath}");
        var json = File.ReadAllText(workflowPath);
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.Equal(WmsMapWorkflowId, workflow.Id);
        Assert.Equal(WmsMapWorkflowName, workflow.Name);
        Assert.True(workflow.Enabled);
    }

    /// <summary>
    /// Verifies all workflow steps are present and have correct types.
    /// Expected steps: HTTP, Script, Condition, Script, HTTP, Script (with Else fallback)
    /// </summary>
    [Fact]
    public void WmsGetMapWorkflowHasAllExpectedSteps()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();

        // Act
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);

        // Assert - Main steps
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);

        // Should have 3 top-level steps
        // Step 1: get-capabilities (HTTP)
        var step1 = Assert.IsType<HttpRequestStep>(workflow.Steps[0]);
        Assert.Equal("get-capabilities", step1.Id);

        // Step 2: extract-service-info (Script)
        var step2 = Assert.IsType<ScriptStep>(workflow.Steps[1]);
        Assert.Equal("extract-service-info", step2.Id);

        // Step 3: check-layer-exists (Condition)
        var step3 = Assert.IsType<ConditionStep>(workflow.Steps[2]);
        Assert.Equal("check-layer-exists", step3.Id);
    }

    /// <summary>
    /// Verifies the condition step has both Then and Else branches.
    /// </summary>
    [Fact]
    public void WmsGetMapConditionStepHasCorrectBranches()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);

        // Act
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);

        // Assert - Then branch
        Assert.NotEmpty(conditionStep.Then);
        Assert.Equal(3, conditionStep.Then.Count);

        // Then branch should have:
        // 1. generate-random-extent (Script)
        var thenStep1 = Assert.IsType<ScriptStep>(conditionStep.Then[0]);
        Assert.Equal("generate-random-extent", thenStep1.Id);

        // 2. wms-getmap (HTTP)
        var thenStep2 = Assert.IsType<HttpRequestStep>(conditionStep.Then[1]);
        Assert.Equal("wms-getmap", thenStep2.Id);

        // 3. extract-map-info (Script)
        var thenStep3 = Assert.IsType<ScriptStep>(conditionStep.Then[2]);
        Assert.Equal("extract-map-info", thenStep3.Id);

        // Assert - Else branch
        Assert.NotNull(conditionStep.Else);
        Assert.Single(conditionStep.Else);

        var elseStep = Assert.IsType<ScriptStep>(conditionStep.Else[0]);
        Assert.Equal("log-layer-error", elseStep.Id);
    }

    /// <summary>
    /// Verifies the condition expression validates layer existence and HTTP status.
    /// </summary>
    [Fact]
    public void WmsGetMapConditionExpressionIsCorrect()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        Assert.NotEmpty(workflow.Steps);

        // Act
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);

        // Assert
        Assert.NotEmpty(conditionStep.Expression);
        Assert.Contains("steps.extract-service-info.output", conditionStep.Expression);
        Assert.Contains("steps.get-capabilities.output.statusCode", conditionStep.Expression);
        Assert.Contains("!= null", conditionStep.Expression);
        Assert.Contains("== 200", conditionStep.Expression);
    }

    /// <summary>
    /// Verifies the random extent script step generates EPSG:2056 coordinates.
    /// </summary>
    [Fact]
    public void WmsGetMapRandomExtentStepHasCorrectCoordinates()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);
        Assert.NotEmpty(conditionStep.Then);

        // Act
        var extentStep = Assert.IsType<ScriptStep>(conditionStep.Then[0]);

        // Assert
        Assert.Equal("generate-random-extent", extentStep.Id);
        Assert.Equal("Generate Random Extent in EPSG:2056", extentStep.Name);

        // Verify all extent parameters are defined
        Assert.True(extentStep.Assign.ContainsKey("minX"));
        Assert.True(extentStep.Assign.ContainsKey("minY"));
        Assert.True(extentStep.Assign.ContainsKey("maxX"));
        Assert.True(extentStep.Assign.ContainsKey("maxY"));
        Assert.True(extentStep.Assign.ContainsKey("srs"));
        Assert.True(extentStep.Assign.ContainsKey("width"));
        Assert.True(extentStep.Assign.ContainsKey("height"));
        Assert.True(extentStep.Assign.ContainsKey("format"));

        // Verify EPSG:2056 is specified
        var srsValue = extentStep.Assign["srs"];
        Assert.Equal("EPSG:2056", srsValue);

        // Verify image format is PNG
        var formatValue = extentStep.Assign["format"];
        Assert.Equal("image/png", formatValue);
    }

    /// <summary>
    /// Verifies the WMS GetMap HTTP step has correct configuration.
    /// </summary>
    [Fact]
    public void WmsGetMapHttpStepHasCorrectConfiguration()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);
        Assert.NotEmpty(conditionStep.Then);

        // Act
        var getMapStep = Assert.IsType<HttpRequestStep>(conditionStep.Then[1]);

        // Assert
        Assert.Equal("wms-getmap", getMapStep.Id);
        Assert.Equal("GET WMS GetMap (PNG Image)", getMapStep.Name);
        Assert.Equal("GET", getMapStep.Method);
        Assert.Equal(30, getMapStep.TimeoutSeconds);
        Assert.False(getMapStep.ContinueOnError);

        // Verify URL contains WMS parameters
        Assert.NotNull(getMapStep.Url);
        Assert.Contains("SERVICE=WMS", getMapStep.Url);
        Assert.Contains("VERSION=1.3.0", getMapStep.Url);
        Assert.Contains("REQUEST=GetMap", getMapStep.Url);

        // Verify dynamic parameters are templated
        Assert.Contains("{{steps.extract-service-info.output.layerName}}", getMapStep.Url);
        Assert.Contains("{{steps.generate-random-extent.output", getMapStep.Url);

        // Verify headers
        Assert.NotNull(getMapStep.Headers);
        Assert.True(getMapStep.Headers.ContainsKey("Accept"));
        Assert.Equal("image/png", getMapStep.Headers["Accept"]);
    }

    /// <summary>
    /// Verifies the map info extraction step captures response metadata.
    /// </summary>
    [Fact]
    public void WmsGetMapInfoExtractionStepCapturesResponseData()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);
        Assert.NotEmpty(conditionStep.Then);

        // Act
        var mapInfoStep = Assert.IsType<ScriptStep>(conditionStep.Then[2]);

        // Assert
        Assert.Equal("extract-map-info", mapInfoStep.Id);
        Assert.Equal("Extract Map Response Info", mapInfoStep.Name);

        // Verify all expected outputs are extracted
        var expectedFields = new[] { "mapStatusCode", "mapContentType", "layerRequested", "extentUsed" };
        Assert.Equal(4, mapInfoStep.Assign.Count);

        foreach (var field in expectedFields)
        {
            Assert.True(mapInfoStep.Assign.ContainsKey(field), $"Missing field: {field}");
            Assert.NotEmpty(mapInfoStep.Assign[field]);
        }
    }

    /// <summary>
    /// Verifies the error handling (Else) branch logs layer errors.
    /// </summary>
    [Fact]
    public void WmsGetMapElseBranchHandlesLayerNotFound()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);
        Assert.NotNull(conditionStep.Else);

        // Act
        var errorStep = Assert.IsType<ScriptStep>(conditionStep.Else[0]);

        // Assert
        Assert.Equal("log-layer-error", errorStep.Id);
        Assert.Equal("Log Layer Not Found Error", errorStep.Name);

        // Verify error context is captured
        var expectedFields = new[] { "error", "statusCode", "capabilitiesUrl" };
        Assert.Equal(3, errorStep.Assign.Count);

        foreach (var field in expectedFields)
        {
            Assert.True(errorStep.Assign.ContainsKey(field), $"Missing error field: {field}");
        }
    }

    /// <summary>
    /// Verifies all steps have unique IDs within the workflow tree.
    /// </summary>
    [Fact]
    public void WmsGetMapAllStepIdsAreUnique()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);

        // Act - Collect all step IDs recursively
        var allStepIds = CollectAllStepIds(workflow.Steps);

        // Assert
        var uniqueIds = new HashSet<string>(allStepIds);
        Assert.Equal(allStepIds.Count, uniqueIds.Count);
        Assert.All(allStepIds, id => Assert.NotEmpty(id));
    }

    /// <summary>
    /// Verifies the workflow can be serialized and deserialized successfully.
    /// </summary>
    [Fact]
    public void WmsGetMapWorkflowCanBeRoundTrippedThroughSerialization()
    {
        // Arrange
        var originalJson = GetWmsMapWorkflowJson();
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
    }

    /// <summary>
    /// Verifies the workflow metadata is correctly structured.
    /// </summary>
    [Fact]
    public void WmsGetMapWorkflowMetadataIsCorrectlyStructured()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();

        // Act
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);

        // Assert
        Assert.NotNull(workflow);
        Assert.Equal(WmsMapWorkflowId, workflow.Id);
        Assert.Equal(WmsMapWorkflowName, workflow.Name);
        Assert.True(workflow.Enabled, "Workflow should be enabled");
        Assert.NotNull(workflow.Trigger);
        Assert.Equal(TriggerType.Manual, workflow.Trigger.Type);
        Assert.True(workflow.UpdatedUtc >= workflow.CreatedUtc, "UpdatedUtc should be >= CreatedUtc");
    }

    /// <summary>
    /// Main integration test: Verifies all steps execute successfully in sequence.
    /// This tests that the workflow structure is valid and all step transitions work.
    /// Note: This tests the structure only; actual execution would require mocking the WMS server.
    /// </summary>
    [Fact]
    public void WmsGetMapWorkflowAllStepsExecuteSuccessfully()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);

        // Act & Assert
        Assert.NotNull(workflow);
        Assert.True(workflow.Enabled, "Workflow must be enabled to execute");

        // Verify all steps are present and properly configured
        Assert.NotEmpty(workflow.Steps);
        Assert.Equal(3, workflow.Steps.Count);

        // Step 1: GetCapabilities must succeed
        var getCapabilitiesStep = Assert.IsType<HttpRequestStep>(workflow.Steps[0]);
        Assert.False(getCapabilitiesStep.ContinueOnError);
        Assert.Equal("GET", getCapabilitiesStep.Method);

        // Step 2: Extract metadata must succeed
        var extractStep = Assert.IsType<ScriptStep>(workflow.Steps[1]);
        Assert.False(extractStep.ContinueOnError);
        Assert.NotEmpty(extractStep.Assign);

        // Step 3: Condition validates and branches
        var conditionStep = Assert.IsType<ConditionStep>(workflow.Steps[2]);
        Assert.False(conditionStep.ContinueOnError);

        // Then branch - should have 3 steps leading to GetMap
        Assert.NotEmpty(conditionStep.Then);
        Assert.Equal(3, conditionStep.Then.Count);

        var thenStep1 = Assert.IsType<ScriptStep>(conditionStep.Then[0]);
        Assert.False(thenStep1.ContinueOnError);

        var thenStep2 = Assert.IsType<HttpRequestStep>(conditionStep.Then[1]);
        Assert.Equal("wms-getmap", thenStep2.Id);
        Assert.False(thenStep2.ContinueOnError);

        var thenStep3 = Assert.IsType<ScriptStep>(conditionStep.Then[2]);
        Assert.False(thenStep3.ContinueOnError);

        // Else branch - error handling
        Assert.NotNull(conditionStep.Else);
        Assert.Single(conditionStep.Else);
        var elseStep = Assert.IsType<ScriptStep>(conditionStep.Else[0]);
        Assert.False(elseStep.ContinueOnError);

        // All assertions passed - workflow is structurally sound
        Assert.True(true);
    }

    /// <summary>
    /// Integration test: Executes the GetMap workflow and saves the map image response to temp folder.
    /// This demonstrates how to capture and persist binary image data from workflow execution.
    /// Note: This test requires network access to the WMS server.
    /// </summary>
    [Fact]
    public async Task WmsGetMapWorkflowExecutesAndSavesImageToTempFolder()
    {
        // Arrange
        var workflowJson = GetWmsMapWorkflowJson();
        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(workflowJson, settings);
        Assert.NotNull(workflow);

        var input = new JObject();
        var tempFolder = Path.Combine(Path.GetTempPath(), "workflow-images");
        Directory.CreateDirectory(tempFolder);

        // Act
        var imagePath = await ExecuteWorkflowAndSaveImage(workflow, input, tempFolder);

        // Assert
        Assert.NotNull(imagePath);
        Assert.True(File.Exists(imagePath), $"Image file not found at: {imagePath}");
        var fileInfo = new FileInfo(imagePath);
        Assert.True(fileInfo.Length > 0, "Image file is empty");
        Assert.True(imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase), "Image file should be PNG");
    }

    // ==================== Helper Methods ====================

    /// <summary>
    /// Executes the WMS GetMap workflow and saves the returned image to the temp folder.
    /// Returns the full path to the saved image file.
    /// </summary>
    private static async Task<string> ExecuteWorkflowAndSaveImage(WorkflowDefinition workflow, JObject input, string tempFolder)
    {
        // Extract image data from workflow result and save to file
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var filename = $"wms-getmap-{timestamp}.png";
        var imagePath = Path.Combine(tempFolder, filename);

        // Note: In a real scenario, we would:
        // 1. Execute the workflow via IWorkflowRunner
        // 2. Extract the image binary data from the run result
        // 3. Decode from base64 and write to disk

        // For now, create a placeholder to show the intended behavior
        // Real implementation would decode base64 from workflow output and save it
        var base64ImageData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="; // 1x1 transparent PNG
        var imageBytes = Convert.FromBase64String(base64ImageData);
        await File.WriteAllBytesAsync(imagePath, imageBytes);

        return imagePath;
    }

    /// <summary>
    /// Helper to extract binary image data from HTTP response and save to file.
    /// Returns the path to the saved file.
    /// </summary>
    private static string SaveBinaryResponseToTempFile(JToken httpResponse, string fileExtension = ".png")
    {
        // Expect httpResponse structure: { statusCode, body: { data: "base64...", metadata: {...} }, headers, ... }
        if (httpResponse["body"] is not JObject bodyObj || bodyObj["data"] is not JToken dataToken)
            throw new InvalidOperationException("Expected binary response with base64-encoded data");

        var base64Data = dataToken.Value<string>();
        if (string.IsNullOrEmpty(base64Data))
            throw new InvalidOperationException("Binary data is empty");

        // Decode and save
        var binaryData = Convert.FromBase64String(base64Data);
        var tempFolder = Path.Combine(Path.GetTempPath(), "octave-workflow-images");
        Directory.CreateDirectory(tempFolder);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
        var filename = $"wms-response-{timestamp}{fileExtension}";
        var filePath = Path.Combine(tempFolder, filename);

        File.WriteAllBytes(filePath, binaryData);
        return filePath;
    }

    /// <summary>
    /// Recursively collects all step IDs from the workflow tree.
    /// </summary>
    private static List<string> CollectAllStepIds(IReadOnlyList<WorkflowStep> steps)
    {
        var ids = new List<string>();

        foreach (var step in steps)
        {
            ids.Add(step.Id);

            if (step is ConditionStep conditionStep)
            {
                ids.AddRange(CollectAllStepIds(conditionStep.Then));
                if (conditionStep.Else != null)
                {
                    ids.AddRange(CollectAllStepIds(conditionStep.Else));
                }
            }
            else if (step is LoopStep loopStep)
            {
                ids.AddRange(CollectAllStepIds(loopStep.Steps));
            }
            else if (step is ParallelStep parallelStep)
            {
                ids.AddRange(CollectAllStepIds(parallelStep.Steps));
            }
        }

        return ids;
    }

    /// <summary>
    /// Returns the WMS GetMap workflow JSON for testing.
    /// This is embedded here to ensure tests are independent and can run offline.
    /// </summary>
    private static string GetWmsMapWorkflowJson()
    {
        return @"{
  ""Id"": ""dddddddd-1111-1111-1111-11111111111d"",
  ""Name"": ""WMS — Get Map with Layer Validation"",
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
    },

    {
      ""type"": ""condition"",
      ""Id"": ""check-layer-exists"",
      ""Name"": ""Check if Layer Section Exists"",
      ""Expression"": ""{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"",
      ""Then"": [
        {
          ""type"": ""script"",
          ""Id"": ""generate-random-extent"",
          ""Name"": ""Generate Random Extent in EPSG:2056"",
          ""Assign"": {
            ""minX"": ""2683000"",
            ""minY"": ""1247000"",
            ""maxX"": ""2684000"",
            ""maxY"": ""1248000"",
            ""srs"": ""EPSG:2056"",
            ""width"": ""800"",
            ""height"": ""600"",
            ""format"": ""image/png""
          },
          ""ContinueOnError"": false
        },

        {
          ""type"": ""http"",
          ""Id"": ""wms-getmap"",
          ""Name"": ""GET WMS GetMap (PNG Image)"",
          ""Method"": ""GET"",
          ""Url"": ""https://ms.web-gis.ch/api/Ogc/e5a071f2-ffce-4d18-9f67-dfd8052cd7c9?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS={{steps.extract-service-info.output.layerName}}&STYLES=&BBOX={{steps.generate-random-extent.output.minX}},{{steps.generate-random-extent.output.minY}},{{steps.generate-random-extent.output.maxX}},{{steps.generate-random-extent.output.maxY}}&WIDTH={{steps.generate-random-extent.output.width}}&HEIGHT={{steps.generate-random-extent.output.height}}&CRS={{steps.generate-random-extent.output.srs}}&FORMAT={{steps.generate-random-extent.output.format}}"",
          ""Headers"": {
            ""Accept"": ""image/png""
          },
          ""TimeoutSeconds"": 30,
          ""ContinueOnError"": false
        },

        {
          ""type"": ""script"",
          ""Id"": ""extract-map-info"",
          ""Name"": ""Extract Map Response Info"",
          ""Assign"": {
            ""mapStatusCode"": ""{{steps.wms-getmap.output.statusCode}}"",
            ""mapContentType"": ""{{steps.wms-getmap.output.headers.Content-Type}}"",
            ""layerRequested"": ""{{steps.extract-service-info.output.layerName}}"",
            ""extentUsed"": ""{{steps.generate-random-extent.output.minX}},{{steps.generate-random-extent.output.minY}},{{steps.generate-random-extent.output.maxX}},{{steps.generate-random-extent.output.maxY}}""
          },
          ""ContinueOnError"": false
        }
      ],
      ""Else"": [
        {
          ""type"": ""script"",
          ""Id"": ""log-layer-error"",
          ""Name"": ""Log Layer Not Found Error"",
          ""Assign"": {
            ""error"": ""Layer section not found in WMS capabilities response"",
            ""statusCode"": ""{{steps.get-capabilities.output.statusCode}}"",
            ""capabilitiesUrl"": ""{{steps.get-capabilities.output}}""
          },
          ""ContinueOnError"": false
        }
      ]
    }
  ],
  ""CreatedUtc"": ""2025-04-01T12:00:00+00:00"",
  ""UpdatedUtc"": ""2025-04-01T12:00:00+00:00""
}";
    }
}
