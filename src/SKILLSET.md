# Doedel.Workflow — Solution Skillset

A lightweight, file-based HTTP workflow engine built on **.NET 10** and **ASP.NET Core**.  
Workflows are stored as JSON files, executed asynchronously in the background, and exposed through a REST API.

---

## Architecture

```
┌──────────────────────────────────────────────────────────┐
│  Doedel.Workflow.Api          (ASP.NET Core Web API)     │
│  ├── WorkflowsController   CRUD for workflow definitions │
│  ├── RunsController        Fetch run results             │
│  └── WebhooksController    Trigger via HTTP webhook      │
├──────────────────────────────────────────────────────────┤
│  Doedel.Workflow.Application  (Class Library)            │
│  ├── Models                WorkflowDefinition, steps …   │
│  ├── Interfaces             Repository + service ports   │
│  └── Services               WorkflowRunner, Templates   │
├──────────────────────────────────────────────────────────┤
│  Doedel.Workflow.Persistence  (Class Library)            │
│  ├── JsonWorkflowRepository  File-backed storage         │
│  ├── JsonRunRepository       File-backed run results     │
│  ├── WorkflowQueue           In-memory channel queue     │
│  ├── WorkflowWorker          Background run executor     │
│  ├── ScheduleWorker          Periodic schedule checker   │
│  └── UrlPolicy               SSRF / host allow-listing  │
└──────────────────────────────────────────────────────────┘
```

---

## Trigger Types

| Trigger      | Description |
|-------------|-------------|
| `Manual`    | Triggered by calling `POST /api/workflows/{id}/runs` |
| `Webhook`   | Triggered by `POST /api/webhooks/{workflowId}/{key}` with a shared secret key |
| `Schedule`  | Triggered automatically by `ScheduleWorker` at a configurable interval (seconds) |

---

## Step Types

| Type        | Description |
|------------|-------------|
| `http`      | Send an HTTP request (GET, POST, PUT, DELETE, …). Supports headers, body, and per-step timeout. |
| `condition` | Evaluate an expression; branch into `then` or `else` step lists. |
| `loop`      | Iterate over a JSON array; each item is available as a named variable. |
| `delay`     | Pause execution for a fixed number of milliseconds. |
| `script`    | Assign one or more computed values into `variables` using template expressions. |
| `parallel`  | Execute multiple steps concurrently; wait for all to complete before continuing. |

---

## Workflow Model

All workflows conform to the **WorkflowDefinition** model, a sealed record containing metadata and an ordered list of steps. Each step inherits from **WorkflowStep** and is polymorphically deserialized based on its `type` field.

### WorkflowDefinition

Root container for a complete workflow.

```csharp
public sealed record WorkflowDefinition(
    Guid Id,                                      // Unique workflow identifier
    string Name,                                  // Human-readable name
    bool Enabled,                                 // Is workflow active?
    TriggerDefinition Trigger,                    // How the workflow is invoked
    ScheduleDefinition? Schedule,                 // Optional recurring schedule
    IReadOnlyList<WorkflowStep> Steps,           // Ordered step list
    DateTimeOffset CreatedUtc,                    // Creation timestamp
    DateTimeOffset UpdatedUtc                     // Last modification timestamp
);
```

**Example:**

```json
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "name": "Daily Sync",
  "enabled": true,
  "trigger": { "type": "Schedule" },
  "schedule": { "intervalSeconds": 86400 },
  "steps": [ /* … */ ],
  "createdUtc": "2024-01-01T00:00:00Z",
  "updatedUtc": "2024-01-01T00:00:00Z"
}
```

### TriggerDefinition

Specifies how the workflow is invoked.

```csharp
public sealed record TriggerDefinition(
    TriggerType Type,              // Manual | Webhook | Schedule
    string? WebhookKey = null      // Required for Webhook type
);

public enum TriggerType { Manual, Webhook, Schedule }
```

### ScheduleDefinition

Configures periodic execution (used when `Trigger.Type == Schedule`).

```csharp
public sealed record ScheduleDefinition(
    int IntervalSeconds,           // Interval between runs (minimum 10)
    DateTimeOffset? StartUtc = null // Optional start time (defaults to now)
);
```

### WorkflowStep (Abstract Base)

All steps inherit from this sealed record. The `type` field (JSON discriminator) determines the concrete step class.

```csharp
public abstract record WorkflowStep(
    string Id,                    // Unique within the workflow
    string? Name,                 // Human-readable description
    bool ContinueOnError = false  // Proceed on step failure?
);
```

### HttpRequestStep

Executes an HTTP request and captures the response.

```csharp
public sealed record HttpRequestStep(
    string Id, string? Name,
    string Method,                                  // GET, POST, PUT, PATCH, DELETE, etc.
    string Url,                                     // Target URL (templated)
    IReadOnlyDictionary<string, string>? Headers = null,
    JToken? Body = null,                           // Request body (format per BodyType)
    BodyType BodyType = BodyType.Json,            // How to encode Body
    int TimeoutSeconds = 30,
    bool ContinueOnError = false
) : WorkflowStep(Id, Name, ContinueOnError);
```

**BodyType Enum:**

```csharp
public enum BodyType
{
    Json,       // application/json (default)
    Xml,        // application/xml
    Text,       // text/plain
    Form,       // application/x-www-form-urlencoded
    Multipart,  // multipart/form-data
    Binary      // application/octet-stream (Base64)
}
```

**Example:**

```json
{
  "type": "http",
  "id": "fetch-user",
  "name": "Fetch User from API",
  "method": "GET",
  "url": "https://api.example.com/users/{{input.userId}}",
  "headers": {
    "Authorization": "Bearer {{input.token}}"
  },
  "timeoutSeconds": 30
}
```

### ConditionStep

Branches execution based on a boolean expression.

```csharp
public sealed record ConditionStep(
    string Id, string? Name,
    string Expression,                           // Template expression returning bool
    IReadOnlyList<WorkflowStep> Then,           // Steps if true
    IReadOnlyList<WorkflowStep>? Else = null,   // Steps if false
    bool ContinueOnError = false
) : WorkflowStep(Id, Name, ContinueOnError);
```

**Example:**

```json
{
  "type": "condition",
  "id": "check-status",
  "expression": "{{steps.fetch-user.output.statusCode}} == 200",
  "then": [
    {
      "type": "script",
      "id": "save-user",
      "assign": { "user": "{{steps.fetch-user.output.body}}" }
    }
  ],
  "else": [
    {
      "type": "delay",
      "id": "retry-delay",
      "milliseconds": 5000
    }
  ]
}
```

### LoopStep

Iterates over a collection, executing child steps for each item.

```csharp
public sealed record LoopStep(
    string Id, string? Name,
    string ItemsExpression,                     // Template expression returning array
    string ItemVariable,                        // Variable name for current item ($item, $user, etc.)
    IReadOnlyList<WorkflowStep> Steps,         // Steps to execute per iteration
    int MaxIterations = 100,                    // Safety limit
    bool ContinueOnError = false
) : WorkflowStep(Id, Name, ContinueOnError);
```

**Example:**

```json
{
  "type": "loop",
  "id": "process-items",
  "itemsExpression": "{{steps.fetch-user.output.body.items}}",
  "itemVariable": "$item",
  "maxIterations": 100,
  "steps": [
    {
      "type": "http",
      "id": "post-item",
      "method": "POST",
      "url": "https://api.example.com/process",
      "body": "{{$item}}"
    }
  ]
}
```

### DelayStep

Pauses workflow execution for a fixed duration.

```csharp
public sealed record DelayStep(
    string Id, string? Name,
    int Milliseconds,                   // Pause duration
    bool ContinueOnError = false
) : WorkflowStep(Id, Name, ContinueOnError);
```

### ScriptStep

Computes and assigns values using template expressions (no external calls).

```csharp
public sealed record ScriptStep(
    string Id, string? Name,
    IReadOnlyDictionary<string, string> Assign,  // Key→templated-value pairs
    bool ContinueOnError = false
) : WorkflowStep(Id, Name, ContinueOnError);
```

**Example:**

```json
{
  "type": "script",
  "id": "compute",
  "assign": {
    "total": "{{steps.loop-process.output.sum}}",
    "timestamp": "{{$now}}",
    "status": "completed"
  }
}
```

### ParallelStep

Runs multiple steps concurrently; waits for all to complete before proceeding.

```csharp
public sealed record ParallelStep(
    string Id, string? Name,
    IReadOnlyList<WorkflowStep> Steps,  // Steps to run in parallel
    bool ContinueOnError = false
) : WorkflowStep(Id, Name, ContinueOnError);
```

**Example:**

```json
{
  "type": "parallel",
  "id": "fetch-all",
  "steps": [
    {
      "type": "http",
      "id": "fetch-users",
      "method": "GET",
      "url": "https://api.example.com/users"
    },
    {
      "type": "http",
      "id": "fetch-products",
      "method": "GET",
      "url": "https://api.example.com/products"
    }
  ]
}
```

### WorkflowRun

Records the execution state and results of a workflow invocation.

```csharp
public sealed record WorkflowRun(
    Guid Id,                                    // Unique run ID
    Guid WorkflowId,                           // Reference to workflow definition
    RunStatus Status,                          // Running | Succeeded | Failed | Skipped
    DateTimeOffset StartedUtc,                 // Execution start
    DateTimeOffset? CompletedUtc,              // Execution end (null if running)
    JObject Input,                             // Payload passed on trigger
    JObject Context,                           // Runtime variables and state
    IReadOnlyList<StepRunResult> Steps,       // Per-step results
    string? ErrorCode = null,
    string? Error = null
);

public enum RunStatus { Running, Succeeded, Failed, Skipped }
```

### StepRunResult

Records execution details for a single step.

```csharp
public sealed record StepRunResult(
    string StepId,                    // Step ID
    RunStatus Status,                 // Step execution status
    DateTimeOffset StartedUtc,        // When step started
    DateTimeOffset CompletedUtc,      // When step completed
    JToken? Output = null,            // Step output (varies by type)
    string? ErrorCode = null,
    string? Error = null
);
```

### JSON Polymorphism

Workflow steps are polymorphically deserialized by **WorkflowStepConverter**, which maps the JSON `type` field to the concrete class:

| `type` field | C# Class | Feature |
|---|---|---|
| `http` / `httprequest` | `HttpRequestStep` | HTTP requests |
| `condition` | `ConditionStep` | Branching |
| `loop` | `LoopStep` | Iteration |
| `delay` | `DelayStep` | Pausing |
| `script` | `ScriptStep` | Data transformation |
| `parallel` | `ParallelStep` | Concurrency |

---

## Template Engine

Every string value in a step (`Url`, `Headers`, `Body`, `Assign`) is resolved at runtime using `{{expression}}` placeholders backed by JSONPath.

### Available context roots

| Root              | Contents |
|------------------|----------|
| `input`           | The JSON payload passed when the run was triggered |
| `steps.<id>.output` | The full output of any previously executed step |
| `variables`       | Values assigned by `script` steps |

### Expression examples

```
{{input.orderId}}
{{steps.fetch-data.output.body.user.email}}
{{steps.fetch-data.output.body.items[0].sku}}
{{steps.fetch-data.output.body.items}}
{{steps.call-api.output.statusCode}}
{{steps.call-api.output.headers.X-Request-Id}}
{{variables.recordCount}}
```

---

## Response Parsing

HTTP step outputs are automatically parsed based on the `Content-Type` of the response.

| Content-Type        | Parsed as |
|--------------------|-----------|
| `application/json`  | Native JSON — all fields directly accessible via dot-notation |
| `application/xml` / `text/xml` | Converted to JSON via `XmlNodeConverter`; attributes exposed with `@` prefix |
| `text/html`         | Parsed via **HtmlAgilityPack** into a structured object (see below) |
| anything else       | Raw string stored in `body` |

### HTML response structure

```json
{
  "title":    "Page title",
  "meta":     { "description": "…", "og:title": "…" },
  "headings": { "h1": ["…"], "h2": ["…", "…"] },
  "links":    [ { "text": "…", "href": "/path" } ],
  "tables":   [ [ ["Header A", "Header B"], ["Val 1", "Val 2"] ] ],
  "text":     "Full visible body text, scripts and styles stripped"
}
```

### Step output shape

```json
{
  "statusCode": 200,
  "isSuccess":  true,
  "headers": {
	"Content-Type":  "application/json",
	"X-Request-Id":  "abc-123"
  },
  "body": { … },
  "certificate": {
	"subject":            "CN=api.example.com, O=Example Inc, C=US",
	"issuer":             "CN=Let's Encrypt Authority X3, O=Let's Encrypt, C=US",
	"serialNumber":       "03A1B2…",
	"thumbprint":         "AABBCC…",
	"notBeforeUtc":       "2025-01-01T00:00:00Z",
	"expiresUtc":         "2025-04-01T00:00:00Z",
	"daysUntilExpiry":    87,
	"isExpired":          false,
	"expiresSoon":        false,
	"signatureAlgorithm": "sha256RSA",
	"publicKeyAlgorithm": "RSA",
	"keySize":            2048,
	"cipherSuite":        "TLS_AES_128_GCM_SHA256",
	"subjectAltNames":    ["api.example.com", "www.example.com"],
	"parsedSubject":      { "CN": "api.example.com", "O": "Example Inc", "C": "US" },
	"parsedIssuer":       { "CN": "Let's Encrypt Authority X3", "O": "Let's Encrypt", "C": "US" }
  }
}
```

> `certificate` is only present for `https://` requests. The probe runs via a dedicated `SslStream` connection and is non-fatal — a failure simply omits the field.

---

## REST API Endpoints

| Method   | Route                              | Description |
|---------|------------------------------------|-------------|
| `GET`    | `/api/workflows`                   | List all workflow definitions |
| `GET`    | `/api/workflows/{id}`              | Get a workflow by ID |
| `POST`   | `/api/workflows`                   | Create a new workflow |
| `PUT`    | `/api/workflows/{id}`              | Update a workflow |
| `DELETE` | `/api/workflows/{id}`              | Delete a workflow |
| `POST`   | `/api/workflows/{id}/runs`         | Trigger a manual run |
| `GET`    | `/api/runs/{id}`                   | Get a run result by ID |
| `POST`   | `/api/webhooks/{workflowId}/{key}` | Trigger a workflow via webhook |
| `GET`    | `/health`                          | Health check |

---

## Storage

All data is persisted as pretty-printed JSON files on disk.

| Path                         | Contents |
|-----------------------------|----------|
| `data/workflows/{id}.json`  | Workflow definitions |
| `data/results/{id}.json`    | Run execution results |

The root path is configurable via `appsettings.json`:

```json
"WorkflowStorage": { "RootPath": "data" }
```

---

## Security

### URL Policy (SSRF Protection)

Outbound HTTP requests are validated before execution:

- Only `https` allowed by default (`AllowHttp: false` overrides this)
- Only hosts listed in `AllowedHosts` may be called
- DNS resolution is performed; requests to private/loopback IP ranges are blocked

```json
"WorkflowSecurity": {
  "AllowedHosts": [ "api.example.com" ],
  "AllowHttp": false
}
```

### Webhook Authentication

Webhook triggers require a per-workflow `WebhookKey` matched using constant-time comparison (`CryptographicOperations.FixedTimeEquals`) to prevent timing attacks.

---

## Validation

Workflows are validated on create and update:

| Rule | Detail |
|------|--------|
| `name` | Required, max 200 characters |
| `steps` | 1 – 100 top-level steps |
| `schedule` | Required when trigger type is `Schedule` |
| `schedule.intervalSeconds` | Minimum 10 seconds |
| Step IDs | Must be unique across the entire workflow tree |

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Runtime | .NET 10 |
| Web framework | ASP.NET Core 10 |
| JSON | Newtonsoft.Json 13 |
| HTML parsing | HtmlAgilityPack 1.13 |
| OpenAPI | Microsoft.AspNetCore.OpenApi |
| Queue | `System.Threading.Channels` (bounded, in-memory) |
| Scheduling | `System.Threading.PeriodicTimer` |
| Testing | xUnit v3 |
| Storage | Local JSON files (no database) |

---

## Projects

| Project | SDK | Role |
|---------|-----|------|
| `Doedel.Workflow.Api` | `Microsoft.NET.Sdk.Web` | HTTP host, controllers, DI wiring |
| `Doedel.Workflow.Application` | `Microsoft.NET.Sdk` | Domain models, interfaces, execution engine |
| `Doedel.Workflow.Persistence` | `Microsoft.NET.Sdk` | File storage, queue, background workers |
| `Doedel.Workflow.UnitTests` | `Microsoft.NET.Sdk` | Unit tests (xUnit v3) |
