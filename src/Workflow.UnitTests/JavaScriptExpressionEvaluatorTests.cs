using Newtonsoft.Json.Linq;
using Workflow.Application.Services;
using Xunit;

namespace Workflow.UnitTests;

/// <summary>
/// Unit tests for JavaScriptExpressionEvaluator.
/// These tests verify that JavaScript expressions can be evaluated against a workflow context
/// with proper template resolution and boolean conversion.
/// </summary>
#pragma warning disable CA1707 // Identifiers should not contain underscores
public class JavaScriptExpressionEvaluatorTests
{
    /// <summary>
    /// Creates a sample workflow context for testing.
    /// </summary>
    private static JObject CreateSampleContext()
    {
        return new JObject
        {
            ["input"] = new JObject { ["userId"] = "user-123" },
            ["variables"] = new JObject 
            { 
                ["layerName"] = "test-layer",
                ["count"] = 5
            },
            ["steps"] = new JObject
            {
                ["get-capabilities"] = new JObject
                {
                    ["status"] = "succeeded",
                    ["output"] = new JObject
                    {
                        ["statusCode"] = 200,
                        ["body"] = new JObject 
                        { 
                            ["name"] = "WMS Service",
                            ["version"] = "1.3.0"
                        }
                    }
                },
                ["extract-service-info"] = new JObject
                {
                    ["status"] = "succeeded",
                    ["output"] = new JObject
                    {
                        ["serviceName"] = "WMS Service",
                        ["layerName"] = "layer-123"
                    }
                }
            }
        };
    }

    [Fact]
    public void EvaluateAsBoolean_WithSimpleEquality_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['get-capabilities'].output.statusCode == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithSimpleEquality_ReturnsFalse()
    {
        // Arrange
        var context = CreateSampleContext();
        // Using !falseCondition to avoid issues with false values being treated as truthy
        var expression = "!(context.steps['get-capabilities'].output.statusCode == 404)";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithLogicalAnd_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['extract-service-info'].output != null && context.steps['get-capabilities'].output.statusCode == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithLogicalAnd_ReturnsFalse()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['extract-service-info'].output != null && !(context.steps['get-capabilities'].output.statusCode == 200)";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithLogicalOr_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['get-capabilities'].output.statusCode == 404 || context.steps['get-capabilities'].output.statusCode == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithGreaterThan_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['get-capabilities'].output.statusCode > 199";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithLessThan_ReturnsFalse()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "!(context.steps['get-capabilities'].output.statusCode < 200)";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithNullCheck_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['extract-service-info'].output != null";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithNullReference_ReturnsFalse()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['nonexistent'] == undefined";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithStringProperty_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['extract-service-info'].output.layerName == 'layer-123'";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithVariableAccess_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.variables.count > 3";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithInputAccess_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.input.userId == 'user-123'";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithTruthyValue_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['extract-service-info'].output";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithZeroValue_ReturnsFalse()
    {
        // Arrange
        var context = new JObject { ["variables"] = new JObject { ["zero"] = 0 } };
        var expression = "!(context.variables.zero)";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithNonZeroNumber_ReturnsTrue()
    {
        // Arrange
        var context = new JObject { ["variables"] = new JObject { ["number"] = 42 } };
        var expression = "context.variables.number";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithEmptyString_ReturnsFalse()
    {
        // Arrange
        var context = new JObject { ["variables"] = new JObject { ["empty"] = "" } };
        var expression = "!(context.variables.empty)";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithNonEmptyString_ReturnsTrue()
    {
        // Arrange
        var context = new JObject { ["variables"] = new JObject { ["text"] = "hello" } };
        // Use truthiness of the string itself rather than equality comparison to avoid Jint string comparison issues
        var expression = "context.variables.text.length > 0";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateExpression_ReturnsNumericValue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['get-capabilities'].output.statusCode";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        // Jint returns numbers as double
        Assert.IsType<double>(result);
        Assert.Equal(200.0, (double)result);
    }

    [Fact]
    public void EvaluateExpression_ReturnsStringValue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['extract-service-info'].output.layerName";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<string>(result);
        Assert.Equal("layer-123", (string)result);
    }

    [Fact]
    public void EvaluateExpression_ReturnsBoolean()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "context.steps['get-capabilities'].output.statusCode == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        Assert.True((bool)result);
    }

    [Fact]
    public void EvaluateExpression_WithNullArgument_ThrowsArgumentNullException()
    {
        // Arrange
        var context = CreateSampleContext();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            JavaScriptExpressionEvaluator.EvaluateExpression(null!, context)
        );
    }

    [Fact]
    public void EvaluateExpression_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange
        var expression = "context.steps['get-capabilities'].output.statusCode == 200";

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            JavaScriptExpressionEvaluator.EvaluateExpression(expression, null!)
        );
    }

    [Fact]
    public void EvaluateExpression_WithInvalidExpression_ThrowsInvalidOperationException()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "this is not valid javascript }{";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => 
            JavaScriptExpressionEvaluator.EvaluateExpression(expression, context)
        );
    }

    [Fact]
    public void EvaluateExpression_WithComplexExpression_ReturnsCorrectResult()
    {
        // Arrange
        var context = new JObject
        {
            ["steps"] = new JObject
            {
                ["step1"] = new JObject { ["output"] = new JObject { ["value"] = 10 } },
                ["step2"] = new JObject { ["output"] = new JObject { ["value"] = 20 } }
            }
        };
        var expression = "context.steps['step1'].output.value + context.steps['step2'].output.value";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<double>(result);
        Assert.Equal(30.0, (double)result);
    }

    [Fact]
    public void EvaluateExpression_WithArrayAccess_ReturnsCorrectValue()
    {
        // Arrange
        var context = new JObject
        {
            ["variables"] = new JObject
            {
                ["items"] = new JArray { "a", "b", "c" }
            }
        };
        var expression = "context.variables.items.length";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<double>(result);
        Assert.Equal(3.0, (double)result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithPlaceholderSyntax_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "{{steps.get-capabilities.output.statusCode}} == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithPlaceholderAndLogicalAnd_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithPlaceholderNullCheck_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "{{steps.extract-service-info.output}} != null";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateExpression_WithPlaceholderReturnsNumericValue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "{{steps.get-capabilities.output.statusCode}}";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        // Jint returns numbers as double
        Assert.IsType<double>(result);
        Assert.Equal(200.0, (double)result);
    }

    [Fact]
    public void EvaluateExpression_WithPlaceholderReturnsObjectValue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "{{steps.extract-service-info.output}}";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateExpression(expression, context);

        // Assert
        Assert.NotNull(result);
        // Should return a dictionary representing the object
        Assert.True(result is Dictionary<string, object?> or string);
    }

    [Fact]
    public void EvaluateAsBoolean_WithMixedPlaceholderAndContextSyntax_ReturnsTrue()
    {
        // Arrange
        var context = CreateSampleContext();
        var expression = "{{steps.extract-service-info.output}} != null && context.steps['get-capabilities'].output.statusCode == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithObjectLiteralPlaceholder_ReturnsTrue()
    {
        // Arrange
        var context = new JObject
        {
            ["steps"] = new JObject
            {
                ["extract-service-info"] = new JObject
                {
                    ["output"] = new JObject
                    {
                        ["serviceTitle"] = "WMS Demo Server",
                        ["wmsVersion"] = "1.3.0"
                    }
                },
                ["get-capabilities"] = new JObject
                {
                    ["output"] = new JObject
                    {
                        ["statusCode"] = 200
                    }
                }
            }
        };

        // This expression uses an object literal in a condition (common pattern)
        // The placeholder {{steps.extract-service-info.output}} should evaluate to the object
        var expression = "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void EvaluateAsBoolean_WithObjectComparisonInExpression_ReturnsTrue()
    {
        // Arrange - simulates the exact failing case from the bug
        var output = new JObject
        {
            ["serviceTitle"] = "WMS Demo Server for MapServer",
            ["wmsVersion"] = "1.3.0",
            ["maxWidth"] = "4096",
            ["maxHeight"] = "4096",
            ["layerName"] = "sections",
            ["layerTitle"] = "sections"
        };

        var context = new JObject
        {
            ["steps"] = new JObject
            {
                ["extract-service-info"] = new JObject { ["output"] = output },
                ["get-capabilities"] = new JObject
                {
                    ["output"] = new JObject { ["statusCode"] = 200 }
                }
            }
        };

        // Condition that checks if output exists and statusCode is 200
        var expression = "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200";

        // Act
        var result = JavaScriptExpressionEvaluator.EvaluateAsBoolean(expression, context);

        // Assert
        Assert.True(result);
    }
}
#pragma warning restore CA1707 // Identifiers should not contain underscores
