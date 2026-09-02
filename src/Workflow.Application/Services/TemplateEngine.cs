using Newtonsoft.Json.Linq;

namespace Workflow.Application.Services;

public static class TemplateEngine
{
    public static string Resolve(string text, JObject context)
    {
        var result = text;
        var start = result.IndexOf("{{", StringComparison.Ordinal);
        while (start >= 0)
        {
            var end = result.IndexOf("}}", start + 2, StringComparison.Ordinal);
            if (end < 0) break;

            var expression = result[(start + 2)..end].Trim();
            string replacement;

            if (TryResolveBuiltIn(expression, out var builtIn))
                replacement = builtIn!;
            else
            {
                var token = context.SelectToken(expression, errorWhenNoMatch: false);
                replacement = token?.Type == JTokenType.String
                    ? token.Value<string>()!
                    : token?.Type is JTokenType.Object or JTokenType.Array
                        ? $"({token.ToString(Newtonsoft.Json.Formatting.None)})"
                        : token?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
            }

            result = result[..start] + replacement + result[(end + 2)..];
            start = result.IndexOf("{{", start, StringComparison.Ordinal);
        }
        return result;
    }

    public static JToken Resolve(JToken token, JObject context) => token switch
    {
        JValue value when value.Type == JTokenType.String
            => new JValue(Resolve(value.Value<string>()!, context)),
        JObject obj
            => new JObject(obj.Properties().Select(p => new JProperty(p.Name, Resolve(p.Value, context)))),
        JArray array
            => new JArray(array.Select(x => Resolve(x, context))),
        _ => token.DeepClone()
    };

    /// <summary>
    /// Resolves built-in <c>$function</c> expressions.
    /// Supports both parameterless functions and parameterized functions:
    /// - Parameterless: $guid, $now, $utcnow, $timestamp, $timestampMs, $date
    /// - Parameterized: $random(min,max) - generates random integer between min and max inclusive
    /// 
    /// Returns <c>true</c> and sets <paramref name="value"/> when the expression is recognised.
    /// </summary>
    private static bool TryResolveBuiltIn(string expression, out string? value)
    {
        var now = DateTimeOffset.UtcNow;

        // Check for parameterized functions first
        if (expression.StartsWith("$random(", StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveRandomFunction(expression, out value))
                return true;
        }

        // Check for parameterless functions
        value = expression switch
        {
            "$guid"        => Guid.NewGuid().ToString("D"),
            "$now"         => DateTimeOffset.Now.ToString("O"),
            "$utcnow"      => now.ToString("O"),
            "$timestamp"   => now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "$timestampMs" => now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "$date"        => now.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            _              => null
        };
        return value is not null;
    }

    /// <summary>
    /// Parses and resolves the $random(min,max) function.
    /// Generates a random integer between min and max (inclusive).
    /// 
    /// Examples:
    /// - $random(1,10) → random integer 1-10
    /// - $random(2683000,2690000) → random integer for Swiss coordinates
    /// 
    /// Returns true and sets value if successful, false otherwise.
    /// </summary>
    private static bool TryResolveRandomFunction(string expression, out string? value)
    {
        value = null;

        try
        {
            // Parse $random(min,max)
            // Expected format: $random(123,456)
            if (!expression.EndsWith(')'))
                return false;

            var content = expression.Substring("$random(".Length);
            content = content[..^1]; // Remove closing )

            var parts = content.Split(',');
            if (parts.Length != 2)
                return false;

            // Parse min and max values
            if (!int.TryParse(parts[0].Trim(), out var min))
                return false;

            if (!int.TryParse(parts[1].Trim(), out var max))
                return false;

            // Validate range
            if (min > max)
                return false;

            // Generate random number
            // Use Random.Shared for thread-safe randomness (.NET 6+)
            var randomValue = Random.Shared.Next(min, max + 1); // +1 because Next is exclusive on upper bound
            value = randomValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

