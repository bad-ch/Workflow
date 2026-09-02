# Is the Expression in Condition Step JavaScript Syntax?

## Short Answer: **NO** ❌

The Expression is **NOT JavaScript syntax**. It's a **JSONPath expression** that gets evaluated against a C# `JObject` (JSON structure) using the Newtonsoft.Json library's `SelectToken()` method.

---

## What the Expression Actually Is

### **JSONPath Expression**

The `Expression` property of a `ConditionStep` is a string that represents a **JSONPath query**, not JavaScript code.

**From your workflow:**
```json
"Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

**From the test:**
```csharp
"expression": "x > 5"
```

These are evaluated as **JSONPath paths**, NOT as JavaScript/C# expressions.

---

## How SelectToken Interprets Your Expression

The expression is passed directly to `Newtonsoft.Json.Linq.JObject.SelectToken()`:

```csharp
var value = context.SelectToken(step.Expression, false);
```

### **SelectToken Behavior**

`SelectToken()` treats the expression as a **JSONPath query** and:
1. Navigates the JSON object structure
2. Returns the value at that path
3. Returns `null` if the path doesn't exist

### **Examples of Valid JSONPath Expressions**

| Expression | What It Matches | Returns |
|---|---|---|
| `"steps.get-capabilities.output.statusCode"` | Navigate to that path | `200` (the statusCode value) |
| `"steps.get-capabilities.output"` | Navigate to output object | `{ "statusCode": 200, "headers": {...} }` |
| `"steps.extract-service-info.output.layerName"` | Navigate to layerName | `"layer-123"` (a string) |
| `"variables.myVar"` | Navigate to a variable | Whatever was assigned |
| `"input.userId"` | Navigate to input property | The userId value |

---

## Why Your Current Expression Won't Work

Your expression:
```json
"Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

**Problem:** This contains `{{ }}` template placeholders that are NOT resolved before being passed to `SelectToken()`.

**What happens:**
1. `SelectToken()` receives the literal string: `"{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"`
2. Tries to navigate the JSON object looking for a property literally named `"{{steps.extract-service-info.output}}"`
3. Can't find it (JSON doesn't have such a property)
4. Returns `null`
5. `null` is treated as `false`
6. **Always executes the Else branch** ❌

---

## What You Should Use Instead

### **Option 1: Simple JSONPath (Recommended)**

If you just want to check if something exists:

```json
"Expression": "steps.extract-service-info.output"
```

**How it works:**
- `SelectToken()` navigates to `steps.extract-service-info.output`
- If it exists and is not null → returns the value → converts to `true`
- If it doesn't exist → returns `null` → converts to `false`

### **Option 2: Nested JSONPath**

Check a specific property:

```json
"Expression": "steps.get-capabilities.output.statusCode"
```

**How it works:**
- `SelectToken()` navigates to the statusCode
- Returns the value (e.g., `200`)
- Any non-null value is truthy → `true`
- If missing, returns `null` → `false`

### **Option 3: Multiple Conditions (What You're Trying)**

**❌ This won't work (what you currently have):**
```json
"Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

**Why:** Templates aren't resolved, and `!=` operators aren't supported by JSONPath.

**✅ Workaround 1 - Use Script Step First:**

Create a script step that assigns a boolean variable:

```json
{
  "type": "script",
  "id": "check-conditions",
  "assign": {
	"layerExists": "{{steps.extract-service-info.output}}",
	"statusOk": "{{steps.get-capabilities.output.statusCode}}"
  }
},
{
  "type": "condition",
  "id": "check-layer-exists",
  "expression": "variables.layerExists",
  "then": [ /* ... */ ],
  "else": [ /* ... */ ]
}
```

Then the expression `"variables.layerExists"` will work because:
1. The script step resolves the templates
2. Stores them in `context.variables`
3. The condition reads from `context.variables`

**✅ Workaround 2 - Fix the ExecuteConditionAsync Method:**

Modify the `WorkflowRunner.cs` to resolve templates BEFORE calling SelectToken:

```csharp
private async Task<JToken> ExecuteConditionAsync(ConditionStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
{
	// ⭐ CORRECTED: Resolve templates first
	var resolvedExpression = TemplateEngine.Resolve(step.Expression, context);

	// Then evaluate as JSONPath
	var value = context.SelectToken(resolvedExpression, false);

	var passed = value?.Type == JTokenType.Boolean 
		? value.Value<bool>() 
		: value is not null && value.Type != JTokenType.Null;

	await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);
	return new JObject { ["matched"] = passed };
}
```

This would allow:
```json
"Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

To work because the templates would be resolved to actual values first.

---

## JSONPath Limitations

The `SelectToken()` method supports **JSONPath query syntax**, but it has limitations:

### **What It DOES Support:**
- Property navigation: `"steps.get-capabilities.output"`
- Array indexing: `"steps[0].output"`
- Wildcard matching: `"$.steps[*].status"`
- Recursive descent: `"$..output"`
- Filters: `"$.steps[?(@.status == 'succeeded')]"` (complex syntax)

### **What It DOES NOT Support:**
- Arithmetic operators: `!=`, `==`, `>`, `<`, `&&`, `||`
- JavaScript/C# expressions: `x > 5 ? "yes" : "no"`
- Function calls: `Math.Max(a, b)`
- String manipulation: `"value".substring(0, 2)`

So expressions like these **won't work**:
```json
"expression": "x > 5"                                          // ❌ Comparison operator
"expression": "{{a}} && {{b}}"                                  // ❌ Logical operator + templates
"expression": "steps.get-capabilities.output.statusCode == 200" // ❌ Comparison operator
```

---

## Comparison: JavaScript vs JSONPath

### **JavaScript Expression:**
```javascript
// This is what you might be expecting:
if (steps['extract-service-info'].output != null && steps['get-capabilities'].output.statusCode == 200) {
  // execute then branch
} else {
  // execute else branch
}
```

**Includes:**
- Operators: `!=`, `==`, `&&`, `||`, `>`, `<`
- Variables: `steps`, `output`, `statusCode`
- Function calls, type conversions, etc.

### **JSONPath Expression:**
```jsonpath
// What Octave Workflow actually uses:
steps.extract-service-info.output
```

**Includes:**
- Property navigation with dots or brackets
- Array indexing
- Query filters (advanced)

**Does NOT include:**
- Any operators (`!=`, `==`, `&&`, `||`)
- Any logic
- Any computation

---

## The Real Issue with Your Expression

Your expression in the workflow:
```json
"Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

Has **multiple problems**:

1. **Templates are not resolved first** → SelectToken sees literal `"{{steps.extract-service-info.output}}"`
2. **JSONPath doesn't understand `!=` operator** → Even if templates were resolved, the `!=` would not be interpreted as a comparison
3. **JSONPath doesn't understand `&&` operator** → Logical AND is not supported

---

## Current Workaround: Use a Script Step

**This WILL work:**

```json
{
  "type": "script",
  "id": "validate-layer",
  "assign": {
	"canProceed": "{{steps.extract-service-info.output != null && steps.get-capabilities.output.statusCode == 200}}"
  }
},
{
  "type": "condition",
  "id": "check-layer-exists",
  "expression": "variables.canProceed",
  "then": [ /* ... */ ]
}
```

Wait... actually that won't work either because script steps use `TemplateEngine.Resolve()`, not expression evaluation.

**The actual workaround:**

```json
{
  "type": "script",
  "id": "validate-layer",
  "assign": {
	"serviceOutput": "{{steps.extract-service-info.output}}",
	"statusCode": "{{steps.get-capabilities.output.statusCode}}"
  }
},
{
  "type": "condition",
  "id": "check-conditions",
  "expression": "variables.serviceOutput",  // Check if serviceOutput is not null
  "then": [
	{
	  "type": "condition",
	  "id": "check-status",
	  "expression": "variables.statusCode",  // Check if statusCode is not null/zero
	  "then": [ /* actual map steps */ ]
	}
  ]
}
```

Or, **simplest approach** - just check if the layer exists:

```json
{
  "type": "condition",
  "id": "check-layer-exists",
  "expression": "steps.extract-service-info.output",
  "then": [ /* GetMap steps */ ],
  "else": [ /* Error handling */ ]
}
```

This works because:
1. `SelectToken()` navigates to `steps.extract-service-info.output`
2. If the script step succeeded, it will have output (non-null)
3. Non-null value = truthy = `true`
4. Executes the `then` branch

---

## Summary Table

| Language | Example | Used In Octave? |
|---|---|---|
| **JavaScript** | `a != null && b == 200` | ❌ NO |
| **C#** | `a != null && b == 200` | ❌ NO |
| **JSONPath** | `steps.extract-service-info.output` | ✅ YES |
| **Templates** | `{{steps.get-capabilities.output.statusCode}}` | ✅ YES (in URLs, headers, script assigns) |

---

## The Real Solution (Fix the Code)

If you want to support complex boolean expressions, the code needs to be modified:

**Current implementation (line 291 in WorkflowRunner.cs):**
```csharp
var value = context.SelectToken(step.Expression, false);
```

**Should be:**
```csharp
// 1. Resolve templates first
var resolvedExpression = TemplateEngine.Resolve(step.Expression, context);

// 2. Then evaluate as JSONPath
var value = context.SelectToken(resolvedExpression, false);
```

**OR:**

Use a proper expression evaluator (like NLua, Roslyn, or custom parser) that understands boolean logic, operators, and functions.

---

## Files Involved

| File | Purpose |
|---|---|
| `Octave.Workflow.Application/Services/WorkflowRunner.cs` | Line 291 - Where expression is evaluated |
| `Octave.Workflow.Application/Services/TemplateEngine.cs` | Resolves `{{ }}` templates (not used for conditions) |
| `Octave.Workflow.Api/data/workflows/dddddddd-1111-1111-1111-11111111111d.json` | Your workflow with the problematic expression |

---

## Quick Reference: Valid Expression Syntax

```json
{
  "type": "condition",
  "expression": "steps.get-capabilities.output",           // ✅ Works - JSONPath
  "expression": "variables.layerName",                     // ✅ Works - JSONPath
  "expression": "input.userId",                            // ✅ Works - JSONPath
  "expression": "steps.result[0]",                         // ✅ Works - Array index

  "expression": "{{steps.get-capabilities.output}}",       // ❌ Doesn't work - templates not resolved
  "expression": "x > 5",                                   // ❌ Doesn't work - operators not supported
  "expression": "a == b",                                  // ❌ Doesn't work - operators not supported
  "expression": "a && b",                                  // ❌ Doesn't work - operators not supported
  "expression": "steps.output != null",                    // ❌ Doesn't work - != not supported
}
```

