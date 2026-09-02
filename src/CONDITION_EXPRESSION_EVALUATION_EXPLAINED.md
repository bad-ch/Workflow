# How Condition Step Expressions Are Evaluated

## Overview

The expression from a condition step like:
```
{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200
```

Is evaluated in the workflow execution pipeline. Here's exactly where and how this happens.

---

## The Execution Flow

### Step 1: Expression Definition (Workflow JSON)

**File:** `Octave.Workflow.Api/data/workflows/dddddddd-1111-1111-1111-11111111111d.json`

```json
{
  "type": "condition",
  "Id": "check-layer-exists",
  "Name": "Check if Layer Section Exists",
  "Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200",
  "Then": [ ... ],
  "Else": [ ... ]
}
```

The `Expression` field contains template expressions with `{{ }}` placeholders.

---

## The Code: Where Expression Evaluation Happens

### 1. **WorkflowRunner.cs** - Main Execution Engine

**File:** `Octave.Workflow.Application/Services/WorkflowRunner.cs`

#### Method: `ExecuteConditionAsync`

```csharp
private async Task<JToken> ExecuteConditionAsync(ConditionStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
{
	// ⭐ KEY LINE: Evaluate the expression
	var value = context.SelectToken(step.Expression, false);

	// Determine if condition passed
	var passed = value?.Type == JTokenType.Boolean 
		? value.Value<bool>() 
		: value is not null && value.Type != JTokenType.Null;

	// Execute appropriate branch
	await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);

	// Return result
	return new JObject { ["matched"] = passed };
}
```

**Location:** Lines 292-298 in `WorkflowRunner.cs`

**What It Does:**
1. Takes the `ConditionStep` with the expression string
2. Resolves/selects the expression value from the context
3. Converts to boolean
4. Branches to "Then" or "Else" steps
5. Returns a result object with the matched status

---

### 2. **Template Resolution Process**

Before the expression is evaluated, template placeholders (`{{ }}`) are resolved.

**File:** `Octave.Workflow.Application/Services/TemplateEngine.cs`

#### Method: `Resolve(string text, JObject context)`

```csharp
public static string Resolve(string text, JObject context)
{
	var result = text;

	// ⭐ Loop through all {{ }} placeholders
	var start = result.IndexOf("{{", StringComparison.Ordinal);
	while (start >= 0)
	{
		// Find the closing }}
		var end = result.IndexOf("}}", start + 2, StringComparison.Ordinal);
		if (end < 0) break;

		// Extract the expression inside {{ }}
		var expression = result[(start + 2)..end].Trim();
		string replacement;

		// Check if it's a built-in function
		if (TryResolveBuiltIn(expression, out var builtIn))
			replacement = builtIn!;
		else
		{
			// ⭐ KEY: Use JSONPath to select from context
			var token = context.SelectToken(expression, errorWhenNoMatch: false);
			replacement = token?.Type == JTokenType.String
				? token.Value<string>()!
				: token?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
		}

		// Replace {{ expression }} with the resolved value
		result = result[..start] + replacement + result[(end + 2)..];
		start = result.IndexOf("{{", start, StringComparison.Ordinal);
	}
	return result;
}
```

**Location:** Lines 8-32 in `TemplateEngine.cs`

**How It Works:**
1. Finds all `{{ }}` placeholders in the text
2. For each placeholder:
   - Extracts the expression inside
   - Checks if it's a built-in function (like `$guid`, `$now`)
   - Otherwise uses JSONPath to select from context
   - Replaces the placeholder with the resolved value

---

## Step-by-Step Example

### Your Expression:
```
{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200
```

### Step 1: Initial State

**Expression string (raw):**
```
{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200
```

**Context object (JObject):**
```json
{
  "input": { /* workflow input */ },
  "variables": { /* assigned variables */ },
  "steps": {
	"get-capabilities": {
	  "status": "succeeded",
	  "output": {
		"statusCode": 200,
		"headers": { /* headers */ },
		"body": { /* WMS response */ }
	  }
	},
	"extract-service-info": {
	  "status": "succeeded",
	  "output": { /* extracted data */ }
	}
  }
}
```

### Step 2: Template Resolution

TemplateEngine.Resolve processes the expression string:

**First placeholder:** `{{steps.extract-service-info.output}}`
- Extract: `steps.extract-service-info.output`
- JSONPath select from context
- Resolve to: `{ /* extracted data object */ }`
- As string: `{"field1":"value1",...}`

**Result after first replacement:**
```
{"field1":"value1",...} != null && {{steps.get-capabilities.output.statusCode}} == 200
```

**Second placeholder:** `{{steps.get-capabilities.output.statusCode}}`
- Extract: `steps.get-capabilities.output.statusCode`
- JSONPath select from context
- Resolve to: `200`
- As string: `"200"`

**Final resolved expression string:**
```
{"field1":"value1",...} != null && 200 == 200
```

### Step 3: Expression Evaluation

In `ExecuteConditionAsync`:
```csharp
var value = context.SelectToken(step.Expression, false);
```

Now `step.Expression` is the resolved string, and `SelectToken` is called on it.

**JSONPath evaluation of:** `{"field1":"value1",...} != null && 200 == 200`

This is actually treated as a JSONPath expression, not JavaScript/C# code!

**Note:** The actual evaluation depends on JSONPath's handling of boolean operators.

---

## The Complete Call Stack

```
1. API Request: POST /api/workflows/{id}/runs
   ↓
2. WorkflowRunner.RunAsync()
   ├─ Initialize context with input
   ├─ Initialize variables and steps objects
   ↓
3. ExecuteStepsAsync() - Loop through workflow steps
   ↓
4. ExecuteStepAsync() - Pattern match on step type
   ├─ If ConditionStep → ExecuteConditionAsync()
   ↓
5. ExecuteConditionAsync()
   ├─ Retrieve the ConditionStep.Expression string
   ├─ Call: var value = context.SelectToken(step.Expression, false)
   │  │
   │  └─ ⭐ HERE: Expression templates are NOT resolved first!
   │     The raw expression with {{ }} is passed to SelectToken
   │
   ├─ Interpret result as boolean
   ├─ Branch: 
   │  ├─ If true → ExecuteStepsAsync(step.Then, ...)
   │  └─ If false → ExecuteStepsAsync(step.Else, ...)
   ↓
6. Recursive execution of Then/Else steps
```

---

## Important Discovery: Template Resolution Gap!

**⚠️ CRITICAL FINDING:**

Looking at the code, in `ExecuteConditionAsync`:

```csharp
var value = context.SelectToken(step.Expression, false);
```

The expression is passed **directly to SelectToken** WITHOUT first being resolved through `TemplateEngine.Resolve()`!

This means:
- The expression `{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200`
- Is passed to JSONPath **as-is, with the `{{ }}` still in it**
- JSONPath doesn't understand `{{ }}` syntax

### Comparison: How ScriptStep Does It

**ScriptStep (correctly resolves templates):**
```csharp
private static JToken ExecuteScript(ScriptStep step, JObject context)
{
	var variables = (JObject)context["variables"]!;
	foreach (var assignment in step.Assign)
		// ⭐ Templates ARE resolved here
		variables[assignment.Key] = TemplateEngine.Resolve(assignment.Value, context);
	return variables.DeepClone();
}
```

**ConditionStep (does NOT resolve templates first):**
```csharp
private async Task<JToken> ExecuteConditionAsync(ConditionStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
{
	// ⚠️ Expression is NOT resolved through TemplateEngine first!
	var value = context.SelectToken(step.Expression, false);
	var passed = value?.Type == JTokenType.Boolean ? value.Value<bool>() : value is not null && value.Type != JTokenType.Null;
	await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);
	return new JObject { ["matched"] = passed };
}
```

---

## How It Actually Works (Likely Behavior)

Given this implementation, the expression evaluation probably:

1. **Attempts JSONPath directly on the raw expression string**
2. **Returns null** (because `{{ }}` is not valid JSONPath)
3. **Treats null as false** (via the condition: `value is not null && value.Type != JTokenType.Null`)
4. **Always executes the Else branch**

Or, more likely:

1. The expression needs to be **pre-simplified** before storage
2. Or templates must be resolved **at workflow definition time**
3. Or there's a **different mechanism** for boolean expression evaluation that we haven't found yet

---

## To Properly Implement Template Resolution in Conditions

The correct implementation would be:

```csharp
private async Task<JToken> ExecuteConditionAsync(ConditionStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
{
	// ⭐ CORRECTED: Resolve templates first
	var resolvedExpression = TemplateEngine.Resolve(step.Expression, context);

	// Then evaluate the resolved expression
	var value = context.SelectToken(resolvedExpression, false);
	var passed = value?.Type == JTokenType.Boolean 
		? value.Value<bool>() 
		: value is not null && value.Type != JTokenType.Null;

	await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);
	return new JObject { ["matched"] = passed };
}
```

---

## Files Involved

| File | Purpose | Lines |
|------|---------|-------|
| `Octave.Workflow.Application/Services/WorkflowRunner.cs` | Executes conditions via `ExecuteConditionAsync()` | 292-298 |
| `Octave.Workflow.Application/Services/TemplateEngine.cs` | Resolves `{{ }}` placeholders | 8-32 |
| `Octave.Workflow.Api/data/workflows/dddddddd-1111-1111-1111-11111111111d.json` | Contains the condition expression | Line 60 |
| `Octave.Workflow.Application/Models/WorkflowModels.cs` | Defines `ConditionStep` model | See definition |

---

## Key Code Locations Quick Reference

### Condition Execution
```
File: Octave.Workflow.Application/Services/WorkflowRunner.cs
Method: ExecuteConditionAsync
Lines: 292-298
Key Line: var value = context.SelectToken(step.Expression, false);
```

### Template Resolution
```
File: Octave.Workflow.Application/Services/TemplateEngine.cs
Method: Resolve
Lines: 8-32
Key Method: context.SelectToken(expression, errorWhenNoMatch: false)
```

### Expression Storage
```
File: Octave.Workflow.Api/data/workflows/dddddddd-1111-1111-1111-11111111111d.json
Field: ConditionStep.Expression
Example: "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

---

## Testing the Expression

From `TemplateEngineTests.cs`:

```csharp
[Fact]
public void ResolveReplacesJsonPathValue()
{
	var context = new JObject
	{
		["steps"] = new JObject
		{
			["my-step"] = new JObject
			{
				["output"] = new JObject { ["field"] = "value" }
			}
		}
	};

	var result = TemplateEngine.Resolve("The value is {{steps.my-step.output.field}}", context);

	Assert.Equal("The value is value", result);
}
```

This shows how templates are resolved in expressions.

---

## Summary

**Where condition expressions are evaluated:**
1. **Defined in:** Workflow JSON file
2. **Parsed by:** WorkflowStepConverter (during deserialization)
3. **Stored in:** ConditionStep.Expression property
4. **Evaluated in:** `WorkflowRunner.ExecuteConditionAsync()` method
5. **Using:** `context.SelectToken()` with JSONPath

**The process:**
1. Expression string contains `{{ }}` placeholders
2. Templates should be resolved to values first (via TemplateEngine)
3. Resulting expression is evaluated as JSONPath/boolean
4. Result branches to Then or Else steps

**Potential issue:**
- The current implementation may not fully resolve templates before evaluation
- Would benefit from explicit `TemplateEngine.Resolve()` call before `SelectToken()`

