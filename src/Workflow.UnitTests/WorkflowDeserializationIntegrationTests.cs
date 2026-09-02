using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Workflow.Application.Models;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Integration tests to verify that complete workflow definitions can be deserialized
/// with various JSON formats and edge cases.
/// </summary>
public class WorkflowDeserializationIntegrationTests
{
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
    public void DeserializesCompleteWorkflowWithHttpStep()
    {
        var json = @"{
            ""id"": ""11111111-1111-1111-1111-111111111111"",
            ""name"": ""HTTP Request Workflow"",
            ""enabled"": true,
            ""trigger"": {
                ""type"": ""Manual""
            },
            ""steps"": [
                {
                    ""type"": ""http"",
                    ""id"": ""step-1"",
                    ""name"": ""Call API"",
                    ""method"": ""POST"",
                    ""url"": ""https://api.example.com/endpoint"",
                    ""bodyType"": ""Json"",
                    ""timeoutSeconds"": ""30"",
                    ""continueOnError"": false
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        Assert.Equal("HTTP Request Workflow", workflow.Name);
        Assert.Single(workflow.Steps);

        var httpStep = workflow.Steps[0] as HttpRequestStep;
        Assert.NotNull(httpStep);
        Assert.Equal("POST", httpStep.Method);
        Assert.Equal(BodyType.Json, httpStep.BodyType);
        Assert.Equal(30, httpStep.TimeoutSeconds);
    }

    [Fact]
    public void DeserializesWorkflowWithMixedPropertyCasing()
    {
        var json = @"{
            ""id"": ""22222222-2222-2222-2222-222222222222"",
            ""name"": ""Mixed Casing Workflow"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""steps"": [
                {
                    ""type"": ""http"",
                    ""Id"": ""step-1"",
                    ""Name"": ""Request"",
                    ""Method"": ""GET"",
                    ""Url"": ""https://example.com"",
                    ""BodyType"": ""Xml"",
                    ""TimeoutSeconds"": 60,
                    ""ContinueOnError"": true
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        var httpStep = workflow.Steps[0] as HttpRequestStep;
        Assert.NotNull(httpStep);
        Assert.Equal("step-1", httpStep.Id);
        Assert.Equal("Request", httpStep.Name);
        Assert.Equal(BodyType.Xml, httpStep.BodyType);
        Assert.Equal(60, httpStep.TimeoutSeconds);
        Assert.True(httpStep.ContinueOnError);
    }

    [Fact]
    public void DeserializesWorkflowWithDelayStep()
    {
        var json = @"{
            ""id"": ""33333333-3333-3333-3333-333333333333"",
            ""name"": ""Delay Workflow"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""steps"": [
                {
                    ""type"": ""delay"",
                    ""id"": ""step-1"",
                    ""name"": ""Wait"",
                    ""milliseconds"": ""5000""
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        var delayStep = workflow.Steps[0] as DelayStep;
        Assert.NotNull(delayStep);
        Assert.Equal(5000, delayStep.Milliseconds);
    }

    [Fact]
    public void DeserializesWorkflowWithLoopStep()
    {
        var json = @"{
            ""id"": ""44444444-4444-4444-4444-444444444444"",
            ""name"": ""Loop Workflow"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""steps"": [
                {
                    ""type"": ""loop"",
                    ""id"": ""step-1"",
                    ""name"": ""Iterate"",
                    ""itemsExpression"": ""items"",
                    ""itemVariable"": ""$item"",
                    ""maxIterations"": ""200"",
                    ""steps"": [
                        {
                            ""type"": ""delay"",
                            ""id"": ""step-2"",
                            ""milliseconds"": 1000
                        }
                    ]
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        var loopStep = workflow.Steps[0] as LoopStep;
        Assert.NotNull(loopStep);
        Assert.Equal("items", loopStep.ItemsExpression);
        Assert.Equal("$item", loopStep.ItemVariable);
        Assert.Equal(200, loopStep.MaxIterations);
        Assert.Single(loopStep.Steps);
    }

    [Fact]
    public void DeserializesWorkflowWithConditionStep()
    {
        var json = @"{
            ""id"": ""55555555-5555-5555-5555-555555555555"",
            ""name"": ""Condition Workflow"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""steps"": [
                {
                    ""type"": ""condition"",
                    ""id"": ""step-1"",
                    ""name"": ""Check"",
                    ""expression"": ""$output.success == true"",
                    ""then"": [
                        {
                            ""type"": ""delay"",
                            ""id"": ""step-2"",
                            ""milliseconds"": ""1000""
                        }
                    ],
                    ""else"": [
                        {
                            ""type"": ""delay"",
                            ""id"": ""step-3"",
                            ""milliseconds"": 5000
                        }
                    ]
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        var conditionStep = workflow.Steps[0] as ConditionStep;
        Assert.NotNull(conditionStep);
        Assert.Equal("$output.success == true", conditionStep.Expression);
        Assert.NotEmpty(conditionStep.Then);
        Assert.NotNull(conditionStep.Else);
        Assert.NotEmpty(conditionStep.Else);
    }

    [Fact]
    public void DeserializesWorkflowWithParallelStep()
    {
        var json = @"{
            ""id"": ""66666666-6666-6666-6666-666666666666"",
            ""name"": ""Parallel Workflow"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""steps"": [
                {
                    ""type"": ""parallel"",
                    ""id"": ""step-1"",
                    ""name"": ""Run in Parallel"",
                    ""steps"": [
                        {
                            ""type"": ""http"",
                            ""id"": ""step-2"",
                            ""method"": ""GET"",
                            ""url"": ""https://example.com/1"",
                            ""bodyType"": 0,
                            ""timeoutSeconds"": 30
                        },
                        {
                            ""type"": ""http"",
                            ""id"": ""step-3"",
                            ""method"": ""GET"",
                            ""url"": ""https://example.com/2"",
                            ""bodyType"": ""Json"",
                            ""timeoutSeconds"": ""30""
                        }
                    ]
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        var parallelStep = workflow.Steps[0] as ParallelStep;
        Assert.NotNull(parallelStep);
        Assert.Equal(2, parallelStep.Steps.Count);

        var httpStep1 = parallelStep.Steps[0] as HttpRequestStep;
        Assert.NotNull(httpStep1);
        Assert.Equal(BodyType.Json, httpStep1.BodyType);
        Assert.Equal(30, httpStep1.TimeoutSeconds);

        var httpStep2 = parallelStep.Steps[1] as HttpRequestStep;
        Assert.NotNull(httpStep2);
        Assert.Equal(BodyType.Json, httpStep2.BodyType);
        Assert.Equal(30, httpStep2.TimeoutSeconds);
    }

    [Fact]
    public void DeserializesWorkflowWithComplexNesting()
    {
        var json = @"{
            ""id"": ""77777777-7777-7777-7777-777777777777"",
            ""name"": ""Complex Workflow"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""schedule"": {
                ""intervalSeconds"": 3600,
                ""startUtc"": ""2024-01-01T00:00:00Z""
            },
            ""steps"": [
                {
                    ""type"": ""loop"",
                    ""id"": ""loop-1"",
                    ""itemsExpression"": ""items"",
                    ""itemVariable"": ""item"",
                    ""maxIterations"": ""100"",
                    ""steps"": [
                        {
                            ""type"": ""condition"",
                            ""id"": ""cond-1"",
                            ""expression"": ""check"",
                            ""then"": [
                                {
                                    ""type"": ""http"",
                                    ""id"": ""http-1"",
                                    ""method"": ""POST"",
                                    ""url"": ""https://api.example.com"",
                                    ""bodyType"": ""Form"",
                                    ""timeoutSeconds"": ""45""
                                }
                            ]
                        }
                    ]
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(json, settings);

        Assert.NotNull(workflow);
        Assert.NotNull(workflow.Schedule);
        Assert.Equal(3600, workflow.Schedule.IntervalSeconds);

        var loopStep = workflow.Steps[0] as LoopStep;
        Assert.NotNull(loopStep);
        Assert.Equal(100, loopStep.MaxIterations);

        var conditionStep = loopStep.Steps[0] as ConditionStep;
        Assert.NotNull(conditionStep);

        var httpStep = conditionStep.Then[0] as HttpRequestStep;
        Assert.NotNull(httpStep);
        Assert.Equal(BodyType.Form, httpStep.BodyType);
        Assert.Equal(45, httpStep.TimeoutSeconds);
    }

    [Fact]
    public void HandlesAllBodyTypeEnumValues()
    {
        var bodyTypes = new[] 
        { 
            (0, "Json"), 
            (1, "Xml"), 
            (2, "Text"), 
            (3, "Form"), 
            (4, "Multipart"), 
            (5, "Binary") 
        };

        var settings = GetWorkflowSerializerSettings();

        foreach (var (intValue, stringValue) in bodyTypes)
        {
            // Test with integer value
            var jsonInt = @"{
                ""id"": ""88888888-8888-8888-8888-888888888888"",
                ""name"": ""Test"",
                ""enabled"": true,
                ""trigger"": { ""type"": ""Manual"" },
                ""steps"": [
                    {
                        ""type"": ""http"",
                        ""id"": ""step-1"",
                        ""method"": ""GET"",
                        ""url"": ""https://example.com"",
                        ""bodyType"": " + intValue + @"
                    }
                ],
                ""createdUtc"": ""2024-01-01T00:00:00Z"",
                ""updatedUtc"": ""2024-01-01T00:00:00Z""
            }";

            var workflowInt = JsonConvert.DeserializeObject<WorkflowDefinition>(jsonInt, settings);
            var stepInt = workflowInt?.Steps[0] as HttpRequestStep;
            Assert.NotNull(stepInt);
            Assert.Equal((BodyType)intValue, stepInt.BodyType);

            // Test with string value
            var jsonStr = @"{
                ""id"": ""88888888-8888-8888-8888-888888888888"",
                ""name"": ""Test"",
                ""enabled"": true,
                ""trigger"": { ""type"": ""Manual"" },
                ""steps"": [
                    {
                        ""type"": ""http"",
                        ""id"": ""step-1"",
                        ""method"": ""GET"",
                        ""url"": ""https://example.com"",
                        ""bodyType"": """ + stringValue + @"""
                    }
                ],
                ""createdUtc"": ""2024-01-01T00:00:00Z"",
                ""updatedUtc"": ""2024-01-01T00:00:00Z""
            }";

            var workflowStr = JsonConvert.DeserializeObject<WorkflowDefinition>(jsonStr, settings);
            var stepStr = workflowStr?.Steps[0] as HttpRequestStep;
            Assert.NotNull(stepStr);
            Assert.Equal((BodyType)intValue, stepStr.BodyType);
        }
    }

    [Fact]
    public void SerializedWorkflowCanBeRoundTripped()
    {
        var originalJson = @"{
            ""id"": ""11111111-1111-1111-1111-111111111111"",
            ""name"": ""Roundtrip Test"",
            ""enabled"": true,
            ""trigger"": { ""type"": ""Manual"" },
            ""steps"": [
                {
                    ""type"": ""http"",
                    ""id"": ""step-1"",
                    ""method"": ""POST"",
                    ""url"": ""https://api.example.com"",
                    ""bodyType"": ""Json"",
                    ""timeoutSeconds"": ""45"",
                    ""continueOnError"": true
                }
            ],
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z""
        }";

        var settings = GetWorkflowSerializerSettings();

        // Deserialize
        var workflow = JsonConvert.DeserializeObject<WorkflowDefinition>(originalJson, settings);
        Assert.NotNull(workflow);

        // Re-serialize
        var reserialized = JsonConvert.SerializeObject(workflow, settings);

        // Deserialize again to verify consistency
        var workflow2 = JsonConvert.DeserializeObject<WorkflowDefinition>(reserialized, settings);
        Assert.NotNull(workflow2);

        // Verify data is preserved
        Assert.Equal(workflow.Name, workflow2.Name);
        Assert.Equal(workflow.Enabled, workflow2.Enabled);
        Assert.Equal(workflow.Steps.Count, workflow2.Steps.Count);

        var step1 = workflow.Steps[0] as HttpRequestStep;
        var step2 = workflow2.Steps[0] as HttpRequestStep;

        Assert.NotNull(step1);
        Assert.NotNull(step2);
        Assert.Equal(step1.Method, step2.Method);
        Assert.Equal(step1.Url, step2.Url);
        Assert.Equal(step1.BodyType, step2.BodyType);
        Assert.Equal(step1.TimeoutSeconds, step2.TimeoutSeconds);
        Assert.Equal(step1.ContinueOnError, step2.ContinueOnError);
    }
}
