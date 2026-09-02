using System.Text;
using Newtonsoft.Json;
using Workflow.Application.Models;
using Workflow.Persistence;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Integration tests for the complete workflow serialization/deserialization pipeline.
/// These tests verify that the fix works end-to-end without StackOverflowException.
/// </summary>
public class WorkflowIntegrationTests
{
    private JsonSerializer _serializer = null!;

    public WorkflowIntegrationTests()
    {
        _serializer = JsonSettingsProvider.CreateSerializer();
    }

    /// <summary>
    /// Test complete workflow definition with multiple nested levels.
    /// This is the primary test to verify the stack overflow fix.
    /// </summary>
    [Fact]
    public void SerializeDeserializeComplexWorkflowRoundTripSucceeds()
    {
        // Arrange - Create a complex workflow with nested steps
        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Complex Workflow",
            Enabled: true,
            Trigger: new TriggerDefinition(TriggerType.Manual),
            Schedule: null,
            Steps: new WorkflowStep[]
            {
                new ConditionStep(
                    Id: "step-1",
                    Name: "Check Status",
                    Expression: "response.status == 200",
                    Then: new WorkflowStep[]
                    {
                        new ParallelStep(
                            Id: "step-2",
                            Name: "Process Results",
                            Steps: new WorkflowStep[]
                            {
                                new LoopStep(
                                    Id: "step-3",
                                    Name: "Loop Results",
                                    ItemsExpression: "response.items",
                                    ItemVariable: "item",
                                    Steps: new WorkflowStep[]
                                    {
                                        new HttpRequestStep(
                                            Id: "step-4",
                                            Name: "Send Item",
                                            Method: "POST",
                                            Url: "https://api.example.com/process",
                                            Headers: new Dictionary<string, string>
                                            {
                                                { "Content-Type", "application/json" },
                                                { "Authorization", "Bearer token" }
                                            }
                                        )
                                    }
                                ),
                                new ScriptStep(
                                    Id: "step-5",
                                    Name: "Calculate Metrics",
                                    Assign: new Dictionary<string, string>
                                    {
                                        { "total", "items.length" },
                                        { "processed", "completed.length" }
                                    }
                                )
                            }
                        )
                    },
                    Else: new WorkflowStep[]
                    {
                        new DelayStep(
                            Id: "step-6",
                            Name: "Wait and Retry",
                            Milliseconds: 5000
                        )
                    }
                )
            },
            CreatedUtc: DateTimeOffset.UtcNow,
            UpdatedUtc: DateTimeOffset.UtcNow
        );

        // Act - Serialize
        var json = JsonConvert.SerializeObject(workflow, JsonSettingsProvider.DefaultSettings);

        // Assert - JSON is valid
        Assert.NotNull(json);
        Assert.Contains("\"type\":\"condition\"", json);
        Assert.Contains("\"type\":\"parallel\"", json);
        Assert.Contains("\"type\":\"loop\"", json);
        Assert.Contains("\"type\":\"http\"", json);
        Assert.Contains("\"type\":\"script\"", json);
        Assert.Contains("\"type\":\"delay\"", json);

        // Act - Deserialize (this is where StackOverflowException would occur without the fix)
        using var reader = new JsonTextReader(new StringReader(json));
        var deserialized = _serializer.Deserialize<WorkflowDefinition>(reader);

        // Assert - Round-trip successful
        Assert.NotNull(deserialized);
        Assert.Equal(workflow.Id, deserialized.Id);
        Assert.Equal(workflow.Name, deserialized.Name);
        Assert.Single(deserialized.Steps);

        // Verify nested structure
        var conditionStep = (ConditionStep)deserialized.Steps[0];
        Assert.Single(conditionStep.Then);
        Assert.Single(conditionStep.Else ?? Array.Empty<WorkflowStep>());

        var parallelStep = (ParallelStep)conditionStep.Then[0];
        Assert.Equal(2, parallelStep.Steps.Count);

        var loopStep = (LoopStep)parallelStep.Steps[0];
        Assert.Single(loopStep.Steps);

        var httpStep = (HttpRequestStep)loopStep.Steps[0];
        Assert.Equal("POST", httpStep.Method);
        Assert.Equal(2, httpStep.Headers?.Count ?? 0);

        var scriptStep = (ScriptStep)parallelStep.Steps[1];
        Assert.Equal(2, scriptStep.Assign.Count);

        var delayStep = (DelayStep)conditionStep.Else![0];
        Assert.Equal(5000, delayStep.Milliseconds);
    }

    /// <summary>
    /// Test that large workflows with many steps deserialize correctly.
    /// </summary>
    [Fact]
    public void DeserializeLargeWorkflowWithManyStepsSucceedsWithoutStackOverflow()
    {
        // Arrange - Create a workflow with 50 steps
        var steps = new WorkflowStep[50];
        for (int i = 0; i < 50; i++)
        {
            steps[i] = new HttpRequestStep(
                Id: $"step-{i}",
                Name: $"Request {i}",
                Method: "GET",
                Url: $"https://api.example.com/endpoint-{i}"
            );
        }

        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Large Workflow",
            Enabled: true,
            Trigger: new TriggerDefinition(TriggerType.Schedule),
            Schedule: new ScheduleDefinition(IntervalSeconds: 3600),
            Steps: steps,
            CreatedUtc: DateTimeOffset.UtcNow,
            UpdatedUtc: DateTimeOffset.UtcNow
        );

        // Act
        var json = JsonConvert.SerializeObject(workflow, JsonSettingsProvider.DefaultSettings);
        using var reader = new JsonTextReader(new StringReader(json));
        var deserialized = _serializer.Deserialize<WorkflowDefinition>(reader);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(50, deserialized.Steps.Count);
        foreach (var step in deserialized.Steps)
        {
            Assert.IsType<HttpRequestStep>(step);
        }
    }

    /// <summary>
    /// Test streaming deserialization with large workflow JSON.
    /// </summary>
    [Fact]
    public void DeserializeStreamingLargeWorkflowSucceedsWithoutStackOverflow()
    {
        // Arrange
        var steps = new WorkflowStep[100];
        for (int i = 0; i < 100; i++)
        {
            steps[i] = new DelayStep(
                Id: $"step-{i}",
                Name: $"Delay {i}",
                Milliseconds: 100 * (i + 1)
            );
        }

        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Streaming Workflow",
            Enabled: true,
            Trigger: new TriggerDefinition(TriggerType.Webhook, "webhook-key"),
            Schedule: null,
            Steps: steps,
            CreatedUtc: DateTimeOffset.UtcNow,
            UpdatedUtc: DateTimeOffset.UtcNow
        );

        var json = JsonConvert.SerializeObject(workflow, JsonSettingsProvider.DefaultSettings);

        // Act
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var reader = new StreamReader(stream);
        using var jsonReader = new JsonTextReader(reader);
        var deserialized = _serializer.Deserialize<WorkflowDefinition>(jsonReader);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(100, deserialized.Steps.Count);
    }

    /// <summary>
    /// Test that workflow with all step types deserializes correctly.
    /// </summary>
    [Fact]
    public void DeserializeWorkflowWithAllStepTypesSucceeds()
    {
        // Arrange
        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "All Step Types",
            Enabled: true,
            Trigger: new TriggerDefinition(TriggerType.Manual),
            Schedule: null,
            Steps: new WorkflowStep[]
            {
                new HttpRequestStep(
                    Id: "http",
                    Name: "HTTP Request",
                    Method: "GET",
                    Url: "https://example.com"
                ),
                new ConditionStep(
                    Id: "condition",
                    Name: "Condition",
                    Expression: "x > 0",
                    Then: Array.Empty<WorkflowStep>()
                ),
                new LoopStep(
                    Id: "loop",
                    Name: "Loop",
                    ItemsExpression: "items",
                    ItemVariable: "item",
                    Steps: Array.Empty<WorkflowStep>()
                ),
                new DelayStep(
                    Id: "delay",
                    Name: "Delay",
                    Milliseconds: 1000
                ),
                new ScriptStep(
                    Id: "script",
                    Name: "Script",
                    Assign: new Dictionary<string, string>()
                ),
                new ParallelStep(
                    Id: "parallel",
                    Name: "Parallel",
                    Steps: Array.Empty<WorkflowStep>()
                )
            },
            CreatedUtc: DateTimeOffset.UtcNow,
            UpdatedUtc: DateTimeOffset.UtcNow
        );

        // Act
        var json = JsonConvert.SerializeObject(workflow, JsonSettingsProvider.DefaultSettings);
        using var reader = new JsonTextReader(new StringReader(json));
        var deserialized = _serializer.Deserialize<WorkflowDefinition>(reader);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(6, deserialized.Steps.Count);
        Assert.IsType<HttpRequestStep>(deserialized.Steps[0]);
        Assert.IsType<ConditionStep>(deserialized.Steps[1]);
        Assert.IsType<LoopStep>(deserialized.Steps[2]);
        Assert.IsType<DelayStep>(deserialized.Steps[3]);
        Assert.IsType<ScriptStep>(deserialized.Steps[4]);
        Assert.IsType<ParallelStep>(deserialized.Steps[5]);
    }

    /// <summary>
    /// Test that parallel steps with deeply nested conditions work correctly.
    /// </summary>
    [Fact]
    public void DeserializeParallelWithNestedConditionsSucceedsWithoutStackOverflow()
    {
        // Arrange
        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Parallel with Nested Conditions",
            Enabled: true,
            Trigger: new TriggerDefinition(TriggerType.Manual),
            Schedule: null,
            Steps: new[]
            {
                new ParallelStep(
                    Id: "parallel-1",
                    Name: "Main Parallel",
                    Steps: new WorkflowStep[]
                    {
                        new ConditionStep(
                            Id: "cond-1",
                            Name: "Condition 1",
                            Expression: "branch1 == true",
                            Then: new WorkflowStep[]
                            {
                                new ConditionStep(
                                    Id: "cond-2",
                                    Name: "Nested Condition 1",
                                    Expression: "sub1 == true",
                                    Then: new[] 
                                    {
                                        new HttpRequestStep(
                                            Id: "http-1",
                                            Name: "HTTP 1",
                                            Method: "GET",
                                            Url: "https://example.com/1"
                                        )
                                    }
                                )
                            }
                        ),
                        new ConditionStep(
                            Id: "cond-3",
                            Name: "Condition 2",
                            Expression: "branch2 == true",
                            Then: new WorkflowStep[]
                            {
                                new ConditionStep(
                                    Id: "cond-4",
                                    Name: "Nested Condition 2",
                                    Expression: "sub2 == true",
                                    Then: new[]
                                    {
                                        new HttpRequestStep(
                                            Id: "http-2",
                                            Name: "HTTP 2",
                                            Method: "POST",
                                            Url: "https://example.com/2"
                                        )
                                    }
                                )
                            }
                        )
                    }
                )
            },
            CreatedUtc: DateTimeOffset.UtcNow,
            UpdatedUtc: DateTimeOffset.UtcNow
        );

        // Act
        var json = JsonConvert.SerializeObject(workflow, JsonSettingsProvider.DefaultSettings);
        using var reader = new JsonTextReader(new StringReader(json));
        var deserialized = _serializer.Deserialize<WorkflowDefinition>(reader);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Single(deserialized.Steps);

        var parallelStep = (ParallelStep)deserialized.Steps[0];
        Assert.Equal(2, parallelStep.Steps.Count);

        var cond1 = (ConditionStep)parallelStep.Steps[0];
        var nestedCond1 = (ConditionStep)cond1.Then[0];
        var http1 = (HttpRequestStep)nestedCond1.Then[0];
        Assert.Equal("http-1", http1.Id);

        var cond3 = (ConditionStep)parallelStep.Steps[1];
        var nestedCond2 = (ConditionStep)cond3.Then[0];
        var http2 = (HttpRequestStep)nestedCond2.Then[0];
        Assert.Equal("http-2", http2.Id);
    }

    /// <summary>
    /// Stress test: deserialize workflow 100 times to verify no memory leaks or state issues.
    /// </summary>
    [Fact]
    public void DeserializeSameWorkflow100TimesAllSucceed()
    {
        // Arrange
        var workflow = new WorkflowDefinition(
            Id: Guid.NewGuid(),
            Name: "Stress Test",
            Enabled: true,
            Trigger: new TriggerDefinition(TriggerType.Manual),
            Schedule: null,
            Steps: new[]
            {
                new ConditionStep(
                    Id: "cond",
                    Name: "Condition",
                    Expression: "true",
                    Then: new[] { new DelayStep("delay", "Delay", 100) }
                )
            },
            CreatedUtc: DateTimeOffset.UtcNow,
            UpdatedUtc: DateTimeOffset.UtcNow
        );

        var json = JsonConvert.SerializeObject(workflow, JsonSettingsProvider.DefaultSettings);

        // Act & Assert
        for (int i = 0; i < 100; i++)
        {
            using var reader = new JsonTextReader(new StringReader(json));
            var deserialized = _serializer.Deserialize<WorkflowDefinition>(reader);

            Assert.NotNull(deserialized);
            Assert.Equal(workflow.Id, deserialized.Id);
            Assert.Single(deserialized.Steps);
        }
    }
}
