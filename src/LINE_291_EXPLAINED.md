# Breaking Down Line 291: var value = context.SelectToken(step.Expression, false);

## The Complete Picture

### Line 291 Context

```csharp
// Line 289: Method signature
private async Task<JToken> ExecuteConditionAsync(ConditionStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
{
	// Line 291: The expression evaluation line
	var value = context.SelectToken(step.Expression, false);

	// Line 292: Convert to boolean
	var passed = value?.Type == JTokenType.Boolean 
		? value.Value<bool>() 
		: value is not null && value.Type != JTokenType.Null;

	// Line 293: Execute appropriate branch
	await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);

	// Line 294: Return result
	return new JObject { ["matched"] = passed };
}
```

---

## What Makes This Line Work: Component Breakdown

### 1. **`context`** - A JObject (JSON structure)

**Type:** `JObject` (from `Newtonsoft.Json.Linq`)

**What it contains:** The entire runtime state of the workflow:

```csharp
var context = new JObject 
{ 
	["input"] = input.DeepClone(),           // Original workflow input
	["variables"] = new JObject(),           // Variables assigned by script steps
	["steps"] = new JObject()                // Results from all executed steps
};
```

**Example during execution:**
```json
{
  "input": {
	"layerId": "layer-123"
  },
  "variables": {},
  "steps": {
	"get-capabilities": {
	  "status": "succeeded",
	  "output": {
		"statusCode": 200,
		"headers": { "content-type": "application/xml" },
		"body": { /* WMS response */ }
	  }
	},
	"extract-service-info": {
	  "status": "succeeded",
	  "output": {
		"serviceName": "WMS Service",
		"layerName": "layer-123"
	  }
	}
  }
}
```

### 2. **`step.Expression`** - A string with JSONPath

**Type:** `string` (property of `ConditionStep`)

**What it contains:** A JSONPath expression (potentially with template placeholders):

```csharp
// From the ConditionStep model definition:
public sealed record ConditionStep(
	string Id, 
	string? Name, 
	string Expression,  // ← This is what we're using on line 291
	IReadOnlyList<WorkflowStep> Then, 
	IReadOnlyList<WorkflowStep>? Else = null,
	bool ContinueOnError = false) : WorkflowStep(Id, Name, ContinueOnError);
```

**Example from your workflow:**
```
"Expression": "{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

Or simplified (if templates were pre-resolved):
```
"Expression": "steps.extract-service-info.output"
```

### 3. **`SelectToken(step.Expression, false)`** - JSONPath Query Method

**What it does:** Searches the JSON structure for tokens matching the JSONPath expression.

**Method signature (from Newtonsoft.Json.Linq):**
```csharp
public static JToken? SelectToken(this JObject obj, string path, bool errorWhenNoMatch = false)
```

**Parameters:**
- **`path`** (`step.Expression`): The JSONPath expression to search for
- **`errorWhenNoMatch`** (`false`): Don't throw an exception if nothing is found; return `null` instead

**Return type:** `JToken?` - The matching token, or `null` if not found

---

## How SelectToken Works: Step-by-Step

### Example 1: Simple Path

**Expression:** `"steps.get-capabilities.output.statusCode"`

**Context:**
```json
{
  "steps": {
	"get-capabilities": {
	  "output": {
		"statusCode": 200,
		"headers": { ... }
	  }
	}
  }
}
```

**Execution:**
```
1. Navigate to context["steps"]
2. Navigate to ["get-capabilities"]
3. Navigate to ["output"]
4. Navigate to ["statusCode"]
5. Return: JValue(200)
```

**Result:**
```csharp
var value = context.SelectToken("steps.get-capabilities.output.statusCode", false);
// value = JValue(200)
// value.Type = JTokenType.Integer
// value.Value<int>() = 200
```

### Example 2: Object Navigation

**Expression:** `"steps.extract-service-info.output"`

**Result:**
```csharp
var value = context.SelectToken("steps.extract-service-info.output", false);
// value = JObject { "serviceName": "WMS Service", "layerName": "layer-123" }
// value.Type = JTokenType.Object
// value.ToString() = "{\"serviceName\":\"WMS Service\",\"layerName\":\"layer-123\"}"
```

### Example 3: Not Found

**Expression:** `"steps.nonexistent.output"`

**Result:**
```csharp
var value = context.SelectToken("steps.nonexistent.output", false);
// value = null
// No exception thrown (because errorWhenNoMatch = false)
```

---

## The Complete Data Flow

### Flow Diagram

```
Workflow JSON File
└── "Expression": "{{steps.extract-service-info.output}} != null && ..."
	│
	├── Deserialized by WorkflowStepConverter
	│
	└── ConditionStep object
		└── Expression property = "{{steps.extract-service-info.output}} != null && ..."
			│
			└── Passed to ExecuteConditionAsync(ConditionStep step, ...)
				│
				├── step.Expression = "{{steps.extract-service-info.output}} != null && ..."
				│
				├── context (JObject) = { "steps": { "extract-service-info": { "output": {...} }, ... } }
				│
				└── Line 291: var value = context.SelectToken(step.Expression, false)
					│
					├── If Expression contains "{{...}}" templates:
					│   └── SelectToken treats it literally (tries to find a property named "{{" which fails)
					│   └── Returns null
					│
					└── If Expression is pure JSONPath:
						└── SelectToken navigates the context tree
						└── Returns the matching JToken
```

---

## What Type Is `value`? (Line 291 Result)

After line 291 executes, `value` can be one of several types:

| JSONPath Result | `value` Type | `value.Type` | Example |
|---|---|---|---|
| Scalar number | `JValue` | `JTokenType.Integer` or `Float` | `200`, `3.14` |
| Scalar string | `JValue` | `JTokenType.String` | `"hello"` |
| Scalar boolean | `JValue` | `JTokenType.Boolean` | `true`, `false` |
| Object (JSON) | `JObject` | `JTokenType.Object` | `{ "key": "value" }` |
| Array (JSON) | `JArray` | `JTokenType.Array` | `[1, 2, 3]` |
| Null/undefined | `null` | N/A | Missing property |

---

## Lines 292-294: What Happens Next

### Line 292: Convert to Boolean
```csharp
var passed = value?.Type == JTokenType.Boolean 
	? value.Value<bool>() 
	: value is not null && value.Type != JTokenType.Null;
```

**Logic:**
1. **If `value` is explicitly a boolean:** Use its boolean value
2. **Otherwise:** Treat it as truthy/falsy
   - `null` → `false`
   - `JTokenType.Null` → `false`
   - Any other non-null value → `true`

**Examples:**
```csharp
// Case 1: Explicit boolean
value = JValue(true);
passed = true;  // ✓ Directly use the boolean value

// Case 2: Truthy object
value = JObject { "name": "service" };
passed = true;  // ✓ Non-null object is truthy

// Case 3: Null
value = null;
passed = false; // ✓ Null is falsy

// Case 4: Zero (still truthy!)
value = JValue(0);
passed = true;  // ✓ Non-null number is truthy (even if zero)
```

### Line 293: Branch Execution
```csharp
await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);
```

- If `passed = true` → Execute `step.Then` steps
- If `passed = false` → Execute `step.Else` steps (or empty array if Else is null)

### Line 294: Return Result
```csharp
return new JObject { ["matched"] = passed };
```

Returns an object indicating whether the condition was matched:
```json
{
  "matched": true
}
```

---

## Why Use SelectToken vs Other Methods?

**SelectToken is ideal because:**

1. **JSONPath Support:** Navigates nested objects with dot notation
   ```csharp
   context.SelectToken("steps.get-capabilities.output.statusCode")
   // vs manual navigation:
   ((JObject)context["steps"])["get-capabilities"]["output"]["statusCode"]
   ```

2. **Safe Navigation:** Returns `null` instead of throwing (with `errorWhenNoMatch: false`)
   ```csharp
   // Won't throw if path doesn't exist
   var value = context.SelectToken("steps.missing.output", false);
   ```

3. **Flexible Paths:** Works with complex expressions
   ```csharp
   "$.steps[0].output"          // First step output
   "$.steps[*].status"          // All step statuses
   "$.steps.my-step.output"     // Specific step
   ```

---

## Complete Example: Execution Trace

### Workflow JSON
```json
{
  "type": "condition",
  "id": "check-layer-exists",
  "expression": "steps.extract-service-info.output",
  "then": [
	{ "type": "script", "id": "log-success", "assign": { "result": "Layer found!" } }
  ],
  "else": [
	{ "type": "script", "id": "log-error", "assign": { "result": "No layer found!" } }
  ]
}
```

### Execution Trace

```
1. ExecuteConditionAsync(step, context, results, ct) called
   ├─ step.Id = "check-layer-exists"
   ├─ step.Expression = "steps.extract-service-info.output"
   └─ context = {
		"steps": {
		  "extract-service-info": {
			"output": { "layerName": "layer-123" }  ← Will find this
		  }
		}
	  }

2. Line 291: var value = context.SelectToken(step.Expression, false)
   ├─ SelectToken searches for "steps.extract-service-info.output"
   ├─ Navigates: context["steps"]["extract-service-info"]["output"]
   └─ Returns: JObject { "layerName": "layer-123" }

3. Line 292: var passed = value?.Type == JTokenType.Boolean ? ... : ...
   ├─ value.Type = JTokenType.Object (it's an object, not a boolean)
   ├─ Evaluates: value is not null && value.Type != JTokenType.Null
   ├─ Result: true && true = true
   └─ passed = true

4. Line 293: await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], ...)
   ├─ passed = true, so execute step.Then
   └─ Executes: log-success script step

5. Line 294: return new JObject { ["matched"] = passed }
   └─ Returns: { "matched": true }

6. step.Id is added to context["steps"]["check-layer-exists"]
   └─ context["steps"]["check-layer-exists"] = {
		"status": "succeeded",
		"output": { "matched": true }
	  }
```

---

## Key Points Summary

| Aspect | Explanation |
|--------|-------------|
| **`context`** | JObject containing entire workflow runtime state (input, variables, step outputs) |
| **`step.Expression`** | String containing JSONPath expression to evaluate |
| **`SelectToken`** | Method that navigates JSON structure using JSONPath, returns matching `JToken` or `null` |
| **Result (`value`)** | Can be `JValue`, `JObject`, `JArray`, or `null` depending on what the expression matches |
| **Boolean conversion** | Explicit booleans are used directly; other values are treated as truthy (non-null) or falsy (null) |
| **Branching** | `passed` value determines whether `Then` or `Else` steps execute |

---

## Important Note: Template Resolution Gap

⚠️ **The current implementation on line 291 does NOT pre-resolve template expressions!**

Your expression:
```
"{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"
```

Is passed directly to `SelectToken()`, which:
- Tries to find a property named `"{{steps.extract-service-info.output}} != null && {{steps.get-capabilities.output.statusCode}} == 200"`
- Doesn't find it
- Returns `null`
- Treats `null` as `false`
- Always executes the `Else` branch!

**To fix this, line 291 should be:**
```csharp
var resolvedExpression = TemplateEngine.Resolve(step.Expression, context);
var value = context.SelectToken(resolvedExpression, false);
```

This would:
1. Replace `{{...}}` templates with actual values
2. Then evaluate the resulting expression with JSONPath
