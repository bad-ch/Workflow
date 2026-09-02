using Newtonsoft.Json;
using Workflow.Application.Models;
using Workflow.Persistence;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Tests for WorkflowStep deserialization to verify the stack overflow fix.
/// These tests ensure that nested WorkflowStep structures deserialize correctly
/// without causing infinite recursion or stack overflow exceptions.
/// </summary>
public class WorkflowStepDeserializationTests
{
    private JsonSerializer _serializer = null!;

    public WorkflowStepDeserializationTests()
    {
        _serializer = JsonSettingsProvider.CreateSerializer();
    }

    /// <summary>
    /// Test basic WorkflowStep deserialization with type field.
    /// </summary>
    [Fact]
    public void DeserializeSimpleHttpStepSucceeds()
    {
        // Arrange
        var json = @"{
            ""type"": ""http"",
            ""id"": ""step-1"",
            ""name"": ""Test HTTP Step"",
            ""method"": ""GET"",
            ""url"": ""https://example.com"",
            ""continueOnError"": false
        }";

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowStep>(reader);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<HttpRequestStep>(result);
        var httpStep = (HttpRequestStep)result;
        Assert.Equal("step-1", httpStep.Id);
        Assert.Equal("Test HTTP Step", httpStep.Name);
        Assert.Equal("GET", httpStep.Method);
        Assert.Equal("https://example.com", httpStep.Url);
    }

    /// <summary>
    /// Test ConditionStep with single nested step (depth 2).
    /// </summary>
    [Fact]
    public void DeserializeConditionStepWithSingleNestedStepSucceeds()
    {
        // Arrange
        var json = @"{
            ""type"": ""condition"",
            ""id"": ""step-1"",
            ""name"": ""Test Condition"",
            ""expression"": ""x > 5"",
            ""then"": [
                {
                    ""type"": ""http"",
                    ""id"": ""step-2"",
                    ""method"": ""POST"",
                    ""url"": ""https://example.com""
                }
            ]
        }";

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowStep>(reader);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ConditionStep>(result);
        var conditionStep = (ConditionStep)result;
        Assert.Equal("step-1", conditionStep.Id);
        Assert.Equal("x > 5", conditionStep.Expression);
        Assert.Single(conditionStep.Then);
        Assert.IsType<HttpRequestStep>(conditionStep.Then[0]);
    }

    /// <summary>
    /// Test LoopStep with nested steps (depth 2).
    /// </summary>
    [Fact]
    public void DeserializeLoopStepWithNestedStepsSucceeds()
    {
        // Arrange
        var json = @"{
            ""type"": ""loop"",
            ""id"": ""step-1"",
            ""name"": ""Test Loop"",
            ""itemsExpression"": ""items"",
            ""itemVariable"": ""item"",
            ""maxIterations"": 100,
            ""steps"": [
                {
                    ""type"": ""http"",
                    ""id"": ""step-2"",
                    ""method"": ""GET"",
                    ""url"": ""https://example.com""
                }
            ]
        }";

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowStep>(reader);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<LoopStep>(result);
        var loopStep = (LoopStep)result;
        Assert.Equal("step-1", loopStep.Id);
        Assert.Equal("items", loopStep.ItemsExpression);
        Assert.Single(loopStep.Steps);
        Assert.IsType<HttpRequestStep>(loopStep.Steps[0]);
    }

    /// <summary>
    /// Test ParallelStep with nested steps (depth 2).
    /// </summary>
    [Fact]
    public void DeserializeParallelStepWithNestedStepsSucceeds()
    {
        // Arrange
        var json = @"{
            ""type"": ""parallel"",
            ""id"": ""step-1"",
            ""name"": ""Test Parallel"",
            ""steps"": [
                {
                    ""type"": ""http"",
                    ""id"": ""step-2"",
                    ""method"": ""GET"",
                    ""url"": ""https://example.com/1""
                },
                {
                    ""type"": ""http"",
                    ""id"": ""step-3"",
                    ""method"": ""GET"",
                    ""url"": ""https://example.com/2""
                }
            ]
        }";

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowStep>(reader);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<ParallelStep>(result);
        var parallelStep = (ParallelStep)result;
        Assert.Equal("step-1", parallelStep.Id);
        Assert.Equal(2, parallelStep.Steps.Count);
        foreach (var s in parallelStep.Steps)
        {
            Assert.IsType<HttpRequestStep>(s);
        }
    }

    /// <summary>
    /// Test deeply nested workflow (ConditionStep > LoopStep > HttpStep, depth 3).
    /// This is the critical test for the stack overflow fix.
    /// </summary>
    [Fact]
    public void DeserializeDeeplyNestedWorkflowSucceedsWithoutStackOverflow()
    {
        // Arrange - Create a workflow definition with nested steps
        var json = @"{
            ""id"": ""550e8400-e29b-41d4-a716-446655440000"",
            ""name"": ""Complex Workflow"",
            ""enabled"": true,
            ""trigger"": {
                ""type"": ""Manual""
            },
            ""schedule"": null,
            ""createdUtc"": ""2024-01-01T00:00:00Z"",
            ""updatedUtc"": ""2024-01-01T00:00:00Z"",
            ""steps"": [
                {
                    ""type"": ""condition"",
                    ""id"": ""cond-1"",
                    ""expression"": ""x > 5"",
                    ""then"": [
                        {
                            ""type"": ""loop"",
                            ""id"": ""loop-1"",
                            ""itemsExpression"": ""items"",
                            ""itemVariable"": ""item"",
                            ""maxIterations"": 100,
                            ""steps"": [
                                {
                                    ""type"": ""http"",
                                    ""id"": ""http-1"",
                                    ""method"": ""POST"",
                                    ""url"": ""https://example.com/process""
                                }
                            ]
                        }
                    ],
                    ""else"": [
                        {
                            ""type"": ""http"",
                            ""id"": ""http-2"",
                            ""method"": ""GET"",
                            ""url"": ""https://example.com/default""
                        }
                    ]
                }
            ]
        }";

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowDefinition>(reader);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), result.Id);
        Assert.Single(result.Steps);

        // Verify the condition step
        Assert.IsType<ConditionStep>(result.Steps[0]);
        var conditionStep = (ConditionStep)result.Steps[0];
        Assert.Single(conditionStep.Then);
        Assert.Single(conditionStep.Else ?? Array.Empty<WorkflowStep>());

        // Verify the loop step inside condition
        Assert.IsType<LoopStep>(conditionStep.Then[0]);
        var loopStep = (LoopStep)conditionStep.Then[0];
        Assert.Single(loopStep.Steps);

        // Verify the http step inside loop
        Assert.IsType<HttpRequestStep>(loopStep.Steps[0]);
        var httpStep = (HttpRequestStep)loopStep.Steps[0];
        Assert.Equal("POST", httpStep.Method);
    }

    /// <summary>
    /// Test multiple levels of nesting (depth 4+) with various step types.
    /// </summary>
    [Fact]
    public void DeserializeMultiLevelNestingSucceedsWithoutStackOverflow()
    {
        // Arrange
        var json = @"{
            ""type"": ""parallel"",
            ""id"": ""parallel-1"",
            ""steps"": [
                {
                    ""type"": ""condition"",
                    ""id"": ""cond-1"",
                    ""expression"": ""x > 5"",
                    ""then"": [
                        {
                            ""type"": ""loop"",
                            ""id"": ""loop-1"",
                            ""itemsExpression"": ""items"",
                            ""itemVariable"": ""item"",
                            ""maxIterations"": 100,
                            ""steps"": [
                                {
                                    ""type"": ""parallel"",
                                    ""id"": ""parallel-2"",
                                    ""steps"": [
                                        {
                                            ""type"": ""http"",
                                            ""id"": ""http-1"",
                                            ""method"": ""GET"",
                                            ""url"": ""https://example.com""
                                        }
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        }";

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowStep>(reader);

        // Assert - Should not throw StackOverflowException
        Assert.NotNull(result);
        Assert.IsType<ParallelStep>(result);

        var parallelStep = (ParallelStep)result;
        Assert.Single(parallelStep.Steps);

        var conditionStep = (ConditionStep)parallelStep.Steps[0];
        var loopStep = (LoopStep)conditionStep.Then[0];
        var innerParallel = (ParallelStep)loopStep.Steps[0];
        var httpStep = (HttpRequestStep)innerParallel.Steps[0];

        Assert.Equal("http-1", httpStep.Id);
    }

    /// <summary>
    /// Test that exceeding maximum nesting depth throws a meaningful error.
    /// </summary>
    [Fact]
    public void DeserializeExceedsMaxDepthThrowsJsonSerializationException()
    {
        // Arrange - Build a JSON with extreme nesting (> 100 levels)
        var json = "{\"type\": \"http\", \"id\": \"1\", \"method\": \"GET\", \"url\": \"https://example.com\"";

        // Wrap it in 101 levels of conditions
        for (int i = 0; i < 101; i++)
        {
            json = $"{{\"type\": \"condition\", \"id\": \"{i}\", \"expression\": \"true\", \"then\": [{json}]}}";
        }

        using var reader = new JsonTextReader(new StringReader(json));

        // Act & Assert
        var ex = Assert.Throws<JsonSerializationException>(
            () => _serializer.Deserialize<WorkflowStep>(reader)
        );

        Assert.Contains("depth exceeded", ex.Message);
    }

    /// <summary>
    /// Test ConditionStep serialization (write path).
    /// </summary>
    [Fact]
    public void SerializeConditionStepWithNestedStepsIncludesTypeField()
    {
        // Arrange
        var step = new ConditionStep(
            Id: "cond-1",
            Name: "Test Condition",
            Expression: "x > 5",
            Then: new[]
            {
                new HttpRequestStep(
                    Id: "http-1",
                    Name: "HTTP Step",
                    Method: "GET",
                    Url: "https://example.com"
                )
            }
        );

        // Act
        var json = JsonConvert.SerializeObject(step, JsonSettingsProvider.DefaultSettings);

        // Assert
        Assert.Contains("\"type\":\"condition\"", json);
        Assert.Contains("\"type\":\"http\"", json);
        Assert.Contains("\"id\":\"cond-1\"", json);

        // Deserialize to verify round-trip works
        using var reader = new JsonTextReader(new StringReader(json));
        var deserialized = _serializer.Deserialize<WorkflowStep>(reader);
        Assert.IsType<ConditionStep>(deserialized);
    }

    /// <summary>
    /// Test that all step types can be deserialized correctly.
    /// </summary>
    [Theory]
    [InlineData("http")]
    [InlineData("condition")]
    [InlineData("loop")]
    [InlineData("delay")]
    [InlineData("script")]
    [InlineData("parallel")]
    public void DeserializeAllStepTypesSucceeds(string stepType)
    {
        // Arrange
        var json = GetJsonForStepType(stepType);

        using var reader = new JsonTextReader(new StringReader(json));

        // Act
        var result = _serializer.Deserialize<WorkflowStep>(reader);

        // Assert
        Assert.NotNull(result);
        var expectedTypeName = stepType switch
        {
            "http" => "HttpRequest",
            "condition" => "Condition",
            "loop" => "Loop",
            "delay" => "Delay",
            "script" => "Script",
            "parallel" => "Parallel",
            _ => throw new ArgumentException($"Unknown step type: {stepType}")
        };
        Assert.Equal(expectedTypeName, result.GetType().Name.Replace("Step", ""));
    }

    private static string GetJsonForStepType(string stepType) => stepType switch
    {
        "http" => @"{
            ""type"": ""http"",
            ""id"": ""1"",
            ""method"": ""GET"",
            ""url"": ""https://example.com""
        }",
        "condition" => @"{
            ""type"": ""condition"",
            ""id"": ""1"",
            ""expression"": ""true"",
            ""then"": []
        }",
        "loop" => @"{
            ""type"": ""loop"",
            ""id"": ""1"",
            ""itemsExpression"": ""items"",
            ""itemVariable"": ""item"",
            ""steps"": []
        }",
        "delay" => @"{
            ""type"": ""delay"",
            ""id"": ""1"",
            ""milliseconds"": 1000
        }",
        "script" => @"{
            ""type"": ""script"",
            ""id"": ""1"",
            ""assign"": {}
        }",
        "parallel" => @"{
            ""type"": ""parallel"",
            ""id"": ""1"",
            ""steps"": []
        }",
        _ => throw new ArgumentException($"Unknown step type: {stepType}")
    };
}
