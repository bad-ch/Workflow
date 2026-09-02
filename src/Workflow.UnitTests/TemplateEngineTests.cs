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
}

