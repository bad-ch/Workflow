# Octave.Workflow

A controller-based .NET 10 / C# 14 backend for defining, storing, scheduling and executing HTTP workflows. No database is used. Workflow definitions and run results are JSON files on disk.

## Projects

- `Octave.Workflow.Api`: Controllers, API configuration, health and OpenAPI endpoints.
- `Octave.Workflow.Application`: API contracts, workflow model, validation, template engine and executor.
- `Octave.Workflow.Persistence`: JSON repositories, queue, scheduler, worker and SSRF URL policy.
- `Octave.Workflow.UnitTests`: xUnit tests.

Dependency direction: `Api -> Application`, `Api -> Persistence`, `Persistence -> Application`.

## Supported steps

- `http`: HTTP/HTTPS request with headers, JSON body and `{{json.path}}` substitutions.
- `condition`: Runs `then` or `else`. The expression is a Newtonsoft JSONPath into the execution context.
- `loop`: Iterates over a JSON array, with a configurable hard iteration limit.
- `delay`: Cancellation-aware delay.
- `script`: Safe declarative variable assignment. It intentionally does not execute arbitrary C#, JavaScript, PowerShell or shell commands.

## Context available to templates

- `input`: Input supplied when the workflow is started.
- `variables`: Values produced by the declarative script step and loop variables.
- `steps.<stepId>.output`: Output of a prior step.

HTTP output contains `statusCode`, `isSuccess` and `body`.

## Run on Windows

1. Install the .NET 10 SDK.
2. Update `WorkflowSecurity:AllowedHosts` in `src/Octave.Workflow.Api/appsettings.json`.
3. From the repository root:

```powershell
dotnet restore .\Octave.Workflow.slnx
dotnet build .\Octave.Workflow.slnx -c Release
dotnet run --project .\src\Octave.Workflow.Api
```

Open `/openapi/v1.json` in Development. Use `Octave.Workflow.Api.http` from Visual Studio or VS Code REST Client.

## API

- `GET /api/workflows`
- `GET /api/workflows/{id}`
- `POST /api/workflows`
- `PUT /api/workflows/{id}`
- `DELETE /api/workflows/{id}`
- `POST /api/workflows/{id}/runs`
- `POST /api/webhooks/{workflowId}/{key}`
- `GET /api/runs/{id}`
- `GET /health`

## Storage

Files are created relative to the API process base path:

```text
data/
|-- workflows/{workflow-id}.json
`-- results/{run-id}.json
```

Writes use a temporary file followed by an atomic rename. This works well for a single application instance. It is not a distributed lock and is not intended for several API instances sharing the same folder.

## Security decisions

- Outbound targets must use HTTPS unless HTTP is explicitly enabled.
- Every outbound hostname must be explicitly allow-listed.
- DNS results are checked to reject loopback, link-local and common private IPv4 ranges.
- Redirects and cookies are disabled in the workflow HTTP client.
- Do not place secrets directly in workflow JSON in production. Resolve credentials from a secret provider by reference.
- The supplied webhook key is a basic example. For production use, store a hash, add rotation, rate limiting, authentication/authorization and audit logging.
- The declarative script step avoids arbitrary remote code execution.

## Important production extensions

1. Add Entra ID JWT authentication and authorization policies.
2. Add rate limiting and API request/body limits.
3. Encrypt sensitive values and use Key Vault references.
4. Persist scheduler next-run state if exact restart behavior is required.
5. Add retry/backoff policies with explicit idempotency rules.
6. Revalidate the connected socket address to fully mitigate DNS rebinding. The supplied DNS check is a strong baseline but not a complete network sandbox.
7. Store detailed internal errors only in structured logs, never in API responses.
