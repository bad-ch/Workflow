using Jint;
using Jint.Native;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace Workflow.Application.Services;

/// <summary>
/// Evaluates JavaScript expressions against a workflow context object.
/// Uses the Jint JavaScript engine to safely evaluate complex boolean expressions.
/// 
/// Supports two expression syntaxes:
/// 1. Template placeholder syntax: {{path.to.property}}
/// 2. Direct context syntax: context.path['to']['property']
/// 
/// Example expressions (placeholder syntax):
/// - {{steps.get-capabilities.output.statusCode}} == 200
/// - {{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200
/// - {{variables.layerName}} && {{variables.layerName}}.length > 0
/// - {{input.userId}} != null
/// 
/// Example expressions (direct context syntax):
/// - context.steps['get-capabilities'].output.statusCode == 200
/// - context.steps['extract-service-info'].output != null && context.steps['get-capabilities'].output.statusCode == 200
/// - context.variables.layerName && context.variables.layerName.length > 0
/// - context.input.userId != null
/// </summary>
public class JavaScriptExpressionEvaluator
{
    /// <summary>
    /// Evaluates a JavaScript expression against the workflow context.
    /// 
    /// The context object is made available as a global variable named "context" in the JavaScript environment.
    /// Properties can be accessed using placeholder syntax or direct context references.
    /// 
    /// Placeholder syntax examples:
    /// - "{{steps.get-capabilities.output.statusCode}} == 200"
    /// - "{{steps.extract-service-info.output}} != null"
    /// 
    /// Direct context syntax examples:
    /// - "context.steps['get-capabilities'].output.statusCode == 200"
    /// - "context.steps['extract-service-info'].output != null"
    /// </summary>
    /// <param name="expression">The JavaScript expression to evaluate</param>
    /// <param name="context">The workflow context object (JObject) containing input, variables, and steps</param>
    /// <returns>The result of the JavaScript expression evaluation as an object</returns>
    /// <exception cref="ArgumentNullException">Thrown if expression or context is null</exception>
    /// <exception cref="InvalidOperationException">Thrown if JavaScript evaluation fails</exception>
    public static object? EvaluateExpression(string expression, JObject context)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            // Replace template placeholders {{path}} with context references
            var processedExpression = ReplacePlaceholders(expression);

            // Create a new Jint engine for this evaluation
            var engine = new Engine(options =>
            {
                // Configure engine for workflow expressions
                options
                    .LimitMemory(10_000_000) // 10 MB memory limit
                    .TimeoutInterval(TimeSpan.FromSeconds(5)) // 5 second timeout
                    .MaxStatements(10_000); // Prevent infinite loops
            });

            // Convert the JObject context to a dynamic object that Jint can work with
            var contextDict = ConvertJObjectToDictionary(context);

            // Set the context as a global variable in the JavaScript engine
            engine.SetValue("context", contextDict);

            // Execute the expression and return the result
            var result = engine.Evaluate(processedExpression);

            // Convert Jint value back to a CLR object
            return JintValueToObject(result);
        }
        catch (Exception ex) when (ex is not ArgumentNullException)
        {
            throw new InvalidOperationException($"Failed to evaluate JavaScript expression '{expression}'", ex);
        }
    }

    /// <summary>
    /// Evaluates a JavaScript expression and converts the result to a boolean.
    /// This is the primary method used for condition step evaluation.
    /// </summary>
    /// <param name="expression">The JavaScript expression to evaluate</param>
    /// <param name="context">The workflow context object containing input, variables, and steps</param>
    /// <returns>Boolean result of the expression evaluation; treats null/undefined as false</returns>
    public static bool EvaluateAsBoolean(string expression, JObject context)
    {
        var result = EvaluateExpression(expression, context);

        return result switch
        {
            null => false,
            bool b => b,
            double d => d != 0,
            int i => i != 0,
            string s => !string.IsNullOrEmpty(s),
            _ => true // Any non-null value is truthy
        };
    }

    /// <summary>
    /// Converts JObject to a dictionary that can be passed to the Jint engine.
    /// This allows JavaScript code to access nested workflow context using dot notation.
    /// </summary>
    private static Dictionary<string, object?> ConvertJObjectToDictionary(JObject obj)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var property in obj.Properties())
        {
            dict[property.Name] = ConvertJTokenToObject(property.Value);
        }

        return dict;
    }

    /// <summary>
    /// Replaces template placeholders {{path.to.property}} with JavaScript context references.
    /// Wraps object/array references in parentheses to ensure valid JavaScript syntax.
    /// Examples:
    /// - {{steps.get-capabilities.output.statusCode}} becomes (context.steps['get-capabilities'].output.statusCode)
    /// - {{variables.layerName}} becomes (context.variables.layerName)
    /// - {{input.userId}} becomes (context.input.userId)
    /// </summary>
    private static string ReplacePlaceholders(string expression)
    {
        if (string.IsNullOrEmpty(expression))
            return expression;

        // Match all {{...}} placeholders
        var pattern = @"\{\{([^}]+)\}\}";
        return Regex.Replace(expression, pattern, (match) =>
        {
            var path = match.Groups[1].Value.Trim();
            var contextRef = ConvertPathToContextReference(path);
            // Wrap in parentheses to ensure valid JavaScript syntax for object/array operations
            return $"({contextRef})";
        });
    }

    /// <summary>
    /// Converts a dot-notation path to a JavaScript context reference.
    /// Handles property names with hyphens by using bracket notation.
    /// Examples:
    /// - "steps.get-capabilities.output" becomes "context.steps['get-capabilities'].output"
    /// - "variables.layerName" becomes "context.variables.layerName"
    /// </summary>
    private static string ConvertPathToContextReference(string path)
    {
        var parts = path.Split('.');
        var result = new System.Text.StringBuilder("context");

        foreach (var part in parts)
        {
            // Use bracket notation if the property name contains hyphens or special characters
            if (part.Contains('-') || !IsValidIdentifier(part))
            {
                result.Append("['" + part + "']");
            }
            else
            {
                result.Append("." + part);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Checks if a string is a valid JavaScript identifier.
    /// Valid identifiers start with a letter, underscore, or dollar sign, 
    /// and contain only letters, digits, underscores, or dollar signs.
    /// </summary>
    private static bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return false;

        // First character must be letter, underscore, or dollar sign
        if (!char.IsLetter(identifier[0]) && identifier[0] != '_' && identifier[0] != '$')
            return false;

        // Remaining characters must be letters, digits, underscores, or dollar signs
        for (int i = 1; i < identifier.Length; i++)
        {
            char c = identifier[i];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '$')
                return false;
        }

        return true;
    }

    /// <summary>
    /// Converts any JToken to a CLR object suitable for Jint's JavaScript engine.
    /// </summary>
    private static object? ConvertJTokenToObject(JToken token)
    {
        return token switch
        {
            JValue value => value.Value,
            JObject obj => ConvertJObjectToDictionary(obj),
            JArray array => array.Select(ConvertJTokenToObject).ToArray(),
            _ => null
        };
    }

    /// <summary>
    /// Converts a Jint JsValue back to a CLR object.
    /// Handles Jint's native types (boolean, number, string).
    /// For complex types (objects, arrays), converts via JSON.
    /// </summary>
    private static object? JintValueToObject(JsValue? jsValue)
    {
        if (jsValue == null || jsValue.IsNull() || jsValue.IsUndefined())
            return null;

        if (jsValue.IsBoolean())
            return jsValue.AsBoolean();

        if (jsValue.IsNumber())
            return jsValue.AsNumber();

        if (jsValue.IsString())
            return jsValue.AsString();

        // For complex objects, convert via JSON string
        // This avoids type mapping issues with Jint's internal types
        try
        {
            var jsonString = jsValue.ToString();
            if (jsonString == "true") return true;
            if (jsonString == "false") return false;
            if (double.TryParse(jsonString, out var num))
                return num;
            return jsonString;
        }
        catch
        {
            // If all else fails, return null
            return null;
        }
    }
}
