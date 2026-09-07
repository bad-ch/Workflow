using Jint;
using Newtonsoft.Json.Linq;

namespace Workflow.Application.Services;

public static class TemplateEngine
{
    /// <summary>
    /// Resolves all <c>{{ }}</c> placeholders in <paramref name="text"/> against the workflow
    /// <paramref name="context"/> and returns the fully-substituted string.
    ///
    /// Three kinds of placeholder are supported inside <c>{{ }}</c>:
    ///
    /// <b>1. Built-in functions</b> – no context lookup required:
    /// <code>
    ///   {{ $guid }}              →  "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
    ///   {{ $utcnow }}            →  "2025-07-01T12:00:00.0000000+00:00"
    ///   {{ $timestamp }}         →  "1751371200"
    ///   {{ $timestampMs }}       →  "1751371200000"
    ///   {{ $date }}              →  "2025-07-01"
    ///   {{ $random(1,100) }}     →  "42"
    /// </code>
    ///
    /// <b>2. JSONPath context lookup</b> – dot-notation path into the workflow context:
    /// <code>
    ///   {{ input.userId }}                          →  value of context.input.userId
    ///   {{ variables.baseUrl }}                     →  value of context.variables.baseUrl
    ///   {{ steps.fetch-user.output.body.name }}     →  value from a previous step's output
    /// </code>
    ///
    /// <b>3. JavaScript expressions</b> – prefix the expression with <c>$js:</c>;
    /// the variables <c>input</c>, <c>variables</c> and <c>steps</c> are available as
    /// top-level JavaScript objects:
    /// <code>
    ///   {{ $js: steps['calc'].output.body.price * 1.08 }}
    ///   {{ $js: input.quantity * variables.unitPrice }}
    ///   {{ $js: Math.round(steps['s'].output.body.lat * 1e6) / 1e6 }}
    ///   {{ $js: steps['a'].output.body.items.length > 0 ? 'yes' : 'no' }}
    ///   {{ $js: [steps['a'].output.body.x, steps['b'].output.body.y].join(',') }}
    /// </code>
    /// </summary>
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
            else if (expression.StartsWith("$js:", StringComparison.OrdinalIgnoreCase))
                replacement = EvaluateJsExpression(expression["$js:".Length..].Trim(), context);
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
    /// <summary>
    /// Evaluates a JavaScript expression with <c>input</c>, <c>variables</c> and <c>steps</c>
    /// bound as top-level globals from the workflow context.
    /// Returns the stringified result, or an empty string on error.
    /// </summary>
    private static string EvaluateJsExpression(string jsExpression, JObject context)
    {
        try
        {
            var engine = new Engine(options =>
            {
                options
                    .LimitMemory(10_000_000)
                    .TimeoutInterval(TimeSpan.FromSeconds(5))
                    .MaxStatements(10_000);
            });

            // Expose input, variables, steps as top-level JS globals
            engine.SetValue("input",     ConvertJTokenToJs(context["input"]     ?? new JObject()));
            engine.SetValue("variables", ConvertJTokenToJs(context["variables"] ?? new JObject()));
            engine.SetValue("steps",     ConvertJTokenToJs(context["steps"]     ?? new JObject()));

            var jsValue = engine.Evaluate(jsExpression);

            if (jsValue.IsNull() || jsValue.IsUndefined()) return "";
            if (jsValue.IsBoolean()) return jsValue.AsBoolean().ToString().ToLowerInvariant();
            if (jsValue.IsNumber())
            {
                var d = jsValue.AsNumber();
                return d == Math.Truncate(d)
                    ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return jsValue.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Converts a <see cref="JToken"/> into a plain CLR object graph that Jint can traverse
    /// using native JavaScript dot/bracket notation.
    /// </summary>
    private static object? ConvertJTokenToJs(JToken token) => token switch
    {
        JObject obj   => obj.Properties().ToDictionary(p => p.Name, p => ConvertJTokenToJs(p.Value)),
        JArray  arr   => arr.Select(ConvertJTokenToJs).ToArray(),
        JValue  val   => val.Value,
        _             => null
    };

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

