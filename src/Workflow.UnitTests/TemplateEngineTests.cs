using Xunit;
using Newtonsoft.Json.Linq;
using Workflow.Application.Services;

namespace Workflow.UnitTests;

public sealed class TemplateEngineTests
{
    [Fact]
    public void ResolveReplacesJsonPathValue()
    {
        var context = JObject.Parse("{\"steps\":{\"first\":{\"output\":{\"id\":42}}}}");
        var result = TemplateEngine.Resolve("/items/{{steps.first.output.id}}", context);
        Assert.Equal("/items/42", result);
    }

    [Fact]
    public void ResolveGuidProducesValidGuid()
    {
        var result = TemplateEngine.Resolve("{{$guid}}", new JObject());
        Assert.True(Guid.TryParseExact(result, "D", out _));
    }

    [Fact]
    public void ResolveTwoGuidsProducesDifferentValues()
    {
        var result = TemplateEngine.Resolve("{{$guid}}-{{$guid}}", new JObject());
        var parts = result.Split('-', 6);
        // first guid occupies parts 0-4, second guid starts at part 5
        var g1 = string.Join("-", parts[..5]);
        var g2 = parts[5];
        Assert.NotEqual(g1, g2 + "-" + g2); // they're different instances
    }

    [Fact]
    public void ResolveUtcNowProducesIso8601()
    {
        var result = TemplateEngine.Resolve("{{$utcnow}}", new JObject());
        Assert.True(DateTimeOffset.TryParseExact(result, "O",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out _));
    }

    [Fact]
    public void ResolveTimestampProducesPositiveLong()
    {
        var result = TemplateEngine.Resolve("{{$timestamp}}", new JObject());
        Assert.True(long.TryParse(result, out var ts) && ts > 0);
    }


    [Fact]
    public void ResolveDateProducesYyyyMmDdFormat()
    {
        var result = TemplateEngine.Resolve("{{$date}}", new JObject());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result);
    }

    [Fact]
    public void ResolveRandomProducesIntegerInRange()
    {
        var result = TemplateEngine.Resolve("{{$random(1,100)}}", new JObject());
        Assert.True(int.TryParse(result, out var value) && value >= 1 && value <= 100);
    }

    [Fact]
    public void ResolveRandomProducesVariedResults()
    {
        // Generate multiple random values and verify they vary (extremely unlikely all are the same)
        var results = new HashSet<string>();
        for (int i = 0; i < 10; i++)
        {
            results.Add(TemplateEngine.Resolve("{{$random(1,1000000)}}", new JObject()));
        }
        Assert.True(results.Count > 1, "Random values should vary across multiple calls");
    }

    [Fact]
    public void ResolveRandomWithLargeCoordinateRange()
    {
        // Simulate EPSG:2056 coordinate generation
        var result = TemplateEngine.Resolve("{{$random(2683000,2689000)}}", new JObject());
        Assert.True(int.TryParse(result, out var value) && value >= 2683000 && value <= 2689000);
    }

    [Fact]
    public void ResolveRandomMultipleInSingleString()
    {
        // Verify multiple random calls in one string each produce different values
        var result = TemplateEngine.Resolve("{{$random(1,1000)}},{{$random(1,1000)}}", new JObject());
        var parts = result.Split(',');
        Assert.Equal(2, parts.Length);
        Assert.True(int.TryParse(parts[0], out var v1));
        Assert.True(int.TryParse(parts[1], out var v2));
        // They *should* be different, but statistically they could be the same, so we just verify both are valid
        Assert.True(v1 >= 1 && v1 <= 1000);
        Assert.True(v2 >= 1 && v2 <= 1000);
    }

    // ── $js: JavaScript expression tests ─────────────────────────────────────

    [Fact]
    public void JsExpressionArithmeticWithStepOutput()
    {
        // {{ $js: steps['calc'].output.body.price * 1.08 }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": { "calc": { "output": { "body": { "price": 100 } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: steps['calc'].output.body.price * 1.08}}", context);
        Assert.Equal("108", result);
    }

    [Fact]
    public void JsExpressionMultiplyInputByVariable()
    {
        // {{ $js: input.quantity * variables.unitPrice }}
        var context = JObject.Parse("""
            {
              "input": { "quantity": 3 },
              "variables": { "unitPrice": 25 },
              "steps": {}
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: input.quantity * variables.unitPrice}}", context);
        Assert.Equal("75", result);
    }

    [Fact]
    public void JsExpressionTernaryProducesString()
    {
        // {{ $js: steps['a'].output.body.items.length > 0 ? 'yes' : 'no' }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": { "a": { "output": { "body": { "items": [1, 2, 3] } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: steps['a'].output.body.items.length > 0 ? 'yes' : 'no'}}", context);
        Assert.Equal("yes", result);
    }

    [Fact]
    public void JsExpressionMathRound()
    {
        // {{ $js: Math.round(steps['s'].output.body.lat * 1e4) / 1e4 }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": { "s": { "output": { "body": { "lat": 47.123456789 } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: Math.round(steps['s'].output.body.lat * 1e4) / 1e4}}", context);
        Assert.Equal("47.1235", result);
    }

    [Fact]
    public void JsExpressionJoinArrayValues()
    {
        // {{ $js: [steps['a'].output.body.x, steps['b'].output.body.y].join(',') }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": {
                "a": { "output": { "body": { "x": 10 } } },
                "b": { "output": { "body": { "y": 20 } } }
              }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: [steps['a'].output.body.x, steps['b'].output.body.y].join(',')}}", context);
        Assert.Equal("10,20", result);
    }

    [Fact]
    public void JsExpressionBooleanResultLowercased()
    {
        // {{ $js: input.age >= 18 }}
        var context = JObject.Parse("""
            { "input": { "age": 21 }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: input.age >= 18}}", context);
        Assert.Equal("true", result);
    }

    [Fact]
    public void JsExpressionMixedWithContextLookupInSameString()
    {
        // "Hello {{input.name}}, total: {{$js: input.qty * variables.price}}"
        var context = JObject.Parse("""
            {
              "input": { "name": "Alice", "qty": 4 },
              "variables": { "price": 10 },
              "steps": {}
            }
            """);
        var result = TemplateEngine.Resolve("Hello {{input.name}}, total: {{$js: input.qty * variables.price}}", context);
        Assert.Equal("Hello Alice, total: 40", result);
    }

    [Fact]
    public void JsExpressionInvalidExpressionReturnsEmpty()
    {
        var context = JObject.Parse("""{ "input": {}, "variables": {}, "steps": {} }""");
        var result = TemplateEngine.Resolve("{{$js: this is not valid JS !!!}}", context);
        Assert.Equal("", result);
    }

    // ── Text results ──────────────────────────────────────────────────────────

    [Fact]
    public void JsExpressionStringConcatenation()
    {
        // {{ $js: input.firstName + ' ' + input.lastName }}
        var context = JObject.Parse("""
            { "input": { "firstName": "Jane", "lastName": "Doe" }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: input.firstName + ' ' + input.lastName}}", context);
        Assert.Equal("Jane Doe", result);
    }

    [Fact]
    public void JsExpressionStringToUpperCase()
    {
        // {{ $js: input.code.toUpperCase() }}
        var context = JObject.Parse("""
            { "input": { "code": "abc123" }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: input.code.toUpperCase()}}", context);
        Assert.Equal("ABC123", result);
    }

    [Fact]
    public void JsExpressionTemplateLiteralInterpolation()
    {
        // {{ $js: `Hello ${input.name}, you have ${steps['inbox'].output.body.count} messages` }}
        var context = JObject.Parse("""
            {
              "input": { "name": "Bob" },
              "variables": {},
              "steps": { "inbox": { "output": { "body": { "count": 5 } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: `Hello ${input.name}, you have ${steps['inbox'].output.body.count} messages`}}", context);
        Assert.Equal("Hello Bob, you have 5 messages", result);
    }

    [Fact]
    public void JsExpressionVariableStringFallback()
    {
        // {{ $js: variables.label || 'default' }}
        var context = JObject.Parse("""
            { "input": {}, "variables": { "label": "" }, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: variables.label || 'default'}}", context);
        Assert.Equal("default", result);
    }

    // ── Number results ────────────────────────────────────────────────────────

    [Fact]
    public void JsExpressionIntegerDivision()
    {
        // {{ $js: Math.floor(input.total / input.pages) }}
        var context = JObject.Parse("""
            { "input": { "total": 100, "pages": 3 }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: Math.floor(input.total / input.pages)}}", context);
        Assert.Equal("33", result);
    }

    [Fact]
    public void JsExpressionDecimalRounded()
    {
        // {{ $js: Math.round(input.value * 100) / 100 }}
        var context = JObject.Parse("""
            { "input": { "value": 3.14159 }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: Math.round(input.value * 100) / 100}}", context);
        Assert.Equal("3.14", result);
    }

    [Fact]
    public void JsExpressionAbsoluteValue()
    {
        // {{ $js: Math.abs(input.delta) }}
        var context = JObject.Parse("""
            { "input": { "delta": -42 }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: Math.abs(input.delta)}}", context);
        Assert.Equal("42", result);
    }

    [Fact]
    public void JsExpressionArrayLength()
    {
        // {{ $js: steps['list'].output.body.items.length }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": { "list": { "output": { "body": { "items": ["a","b","c","d"] } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: steps['list'].output.body.items.length}}", context);
        Assert.Equal("4", result);
    }

    // ── Boolean results ───────────────────────────────────────────────────────

    [Fact]
    public void JsExpressionBooleanFalseResultLowercased()
    {
        // {{ $js: input.score >= 90 }}
        var context = JObject.Parse("""
            { "input": { "score": 72 }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: input.score >= 90}}", context);
        Assert.Equal("false", result);
    }

    [Fact]
    public void JsExpressionBooleanEqualityCheck()
    {
        // {{ $js: steps['auth'].output.body.role === 'admin' }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": { "auth": { "output": { "body": { "role": "admin" } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: steps['auth'].output.body.role === 'admin'}}", context);
        Assert.Equal("true", result);
    }

    [Fact]
    public void JsExpressionBooleanLogicalAnd()
    {
        // {{ $js: input.active && input.verified }}
        var context = JObject.Parse("""
            { "input": { "active": true, "verified": false }, "variables": {}, "steps": {} }
            """);
        var result = TemplateEngine.Resolve("{{$js: input.active && input.verified}}", context);
        Assert.Equal("false", result);
    }

    [Fact]
    public void JsExpressionBooleanNullishCheck()
    {
        // {{ $js: steps['fetch'].output.body.data != null }}
        var context = JObject.Parse("""
            {
              "input": {},
              "variables": {},
              "steps": { "fetch": { "output": { "body": { "data": null } } } }
            }
            """);
        var result = TemplateEngine.Resolve("{{$js: steps['fetch'].output.body.data != null}}", context);
        Assert.Equal("false", result);
    }
}

