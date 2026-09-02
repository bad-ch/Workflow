using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;
namespace Workflow.Application.Services;

public sealed class WorkflowRunner(
    IHttpClientFactory httpClientFactory,
    IUrlPolicy urlPolicy,
    IRunRepository runs,
    ICertificateInspector certificateInspector) : IWorkflowRunner
{
    public async Task<WorkflowRun> RunAsync(Guid runId, WorkflowDefinition workflow, JObject input, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var context = new JObject { ["input"] = input.DeepClone(), ["variables"] = new JObject(), ["steps"] = new JObject() };
        var results = new List<StepRunResult>();
        var run = new WorkflowRun(runId, workflow.Id, RunStatus.Running, started, null, input, context, results);
        await runs.SaveAsync(run, cancellationToken);
        try
        {
            await ExecuteStepsAsync(workflow.Steps, context, results, cancellationToken);
            run = run with { Status = RunStatus.Succeeded, CompletedUtc = DateTimeOffset.UtcNow, Context = context, Steps = results.ToArray() };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run = run with { Status = RunStatus.Failed, CompletedUtc = DateTimeOffset.UtcNow, Context = context, Steps = results.ToArray(), ErrorCode = "workflow_failed", Error = "Workflow execution failed." };
        }
        await runs.SaveAsync(run, cancellationToken);
        return run;
    }

    private async Task ExecuteStepsAsync(IEnumerable<WorkflowStep> steps, JObject context, List<StepRunResult> results, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            var started = DateTimeOffset.UtcNow;
            try
            {
                var output = await ExecuteStepAsync(step, context, results, ct);
                context["steps"]![step.Id] = new JObject { ["status"] = "succeeded", ["output"] = output };
                results.Add(new(step.Id, RunStatus.Succeeded, started, DateTimeOffset.UtcNow, output));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context["steps"]![step.Id] = new JObject { ["status"] = "failed", ["error"] = "Step failed." };
                results.Add(new(step.Id, RunStatus.Failed, started, DateTimeOffset.UtcNow, null, "step_failed", "Step failed."));
                if (!step.ContinueOnError) throw new InvalidOperationException($"Step '{step.Id}' failed.", ex);
            }
        }
    }

    private async Task<JToken?> ExecuteStepAsync(WorkflowStep step, JObject context, List<StepRunResult> results, CancellationToken ct) => step switch
    {
        HttpRequestStep http => await ExecuteHttpAsync(http, context, ct),
        DelayStep delay => await ExecuteDelayAsync(delay, ct),
        ScriptStep script => ExecuteScript(script, context),
        ConditionStep condition => await ExecuteConditionAsync(condition, context, results, ct),
        LoopStep loop => await ExecuteLoopAsync(loop, context, results, ct),
        ParallelStep parallel => await ExecuteParallelAsync(parallel, context, results, ct),
        _ => throw new NotSupportedException()
    };

    private async Task<JToken> ExecuteHttpAsync(HttpRequestStep step, JObject context, CancellationToken ct)
    {
        var url = TemplateEngine.Resolve(step.Url, context);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new InvalidOperationException("Resolved URL is invalid.");
        await urlPolicy.ValidateAsync(uri, ct);
        using var request = new HttpRequestMessage(new HttpMethod(step.Method), uri);
        if (step.Headers is not null)
            foreach (var h in step.Headers)
                if (!request.Headers.TryAddWithoutValidation(h.Key, TemplateEngine.Resolve(h.Value, context)))
                    request.Content?.Headers.TryAddWithoutValidation(h.Key, TemplateEngine.Resolve(h.Value, context));
        if (step.Body is not null)
            request.Content = BuildRequestContent(step.BodyType, step.Body, context);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds));
        var sw = Stopwatch.StartNew();
        using var response = await httpClientFactory.CreateClient("workflow").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        sw.Stop();

        // Check if this is binary content (image, PDF, etc.)
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        var isBinaryContent = contentType.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                            contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
                            contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase);

        // Read response body - as binary for images, as string for text/markup
        JToken bodyToken;
        string? binaryDataPath = null;

        if (isBinaryContent)
        {
            // For binary content, preserve as base64-encoded string and optionally save to temp
            var binaryData = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            bodyToken = Convert.ToBase64String(binaryData);

            // Store metadata about the binary data
            var binaryMetadata = new JObject
            {
                ["encoding"] = "base64",
                ["size"] = binaryData.Length,
                ["contentType"] = contentType
            };
            bodyToken = new JObject
            {
                ["data"] = bodyToken,
                ["metadata"] = binaryMetadata
            };
        }
        else
        {
            // For text-based content, read as string and parse as before
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            // Parse body: JSON → JToken, XML → JToken, HTML → JToken, plain text → JValue
            if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var doc = XDocument.Parse(body);
                    bodyToken = JObject.Parse(JsonConvert.SerializeXNode(doc, Formatting.None, omitRootObject: false));
                }
                catch { bodyToken = body; }
            }
            else if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                try { bodyToken = HtmlResponseParser.Parse(body); }
                catch { bodyToken = body; }
            }
            else
            {
                try { bodyToken = JToken.Parse(body); }
                catch { bodyToken = body; }
            }
        }

        // Collect all response headers (including content headers) into a flat object
        var headers = new JObject();
        foreach (var h in response.Headers.Concat(response.Content.Headers))
            headers[h.Key] = h.Value.Count() == 1 ? (JToken)h.Value.First() : new JArray(h.Value);

        // Probe the TLS certificate (HTTPS only; runs in parallel with body parse, non-fatal)
        var certificate = await certificateInspector.InspectAsync(uri, ct);

        var result = new JObject
        {
            ["statusCode"]    = (int)response.StatusCode,
            ["isSuccess"]     = response.IsSuccessStatusCode,
            ["responseTimeMs"] = sw.ElapsedMilliseconds,
            ["headers"]       = headers,
            ["body"]          = bodyToken
        };

        if (certificate is not null)
            result["certificate"] = certificate;

        if (binaryDataPath is not null)
            result["binaryDataPath"] = binaryDataPath;

        return result;
    }

    /// <summary>
    /// Builds an <see cref="HttpContent"/> instance for the configured <paramref name="bodyType"/>.
    /// All string values inside <paramref name="body"/> are resolved through the template engine first.
    /// </summary>
    private static HttpContent BuildRequestContent(BodyType bodyType, JToken body, JObject context)
    {
        switch (bodyType)
        {
            case BodyType.Json:
            {
                var resolved = TemplateEngine.Resolve(body, context);
                return new StringContent(
                    resolved.ToString(Newtonsoft.Json.Formatting.None),
                    Encoding.UTF8,
                    "application/json");
            }

            case BodyType.Xml:
            {
                var xml = TemplateEngine.Resolve(body.Value<string>() ?? body.ToString(), context);
                return new StringContent(xml, Encoding.UTF8, "application/xml");
            }

            case BodyType.Text:
            {
                var text = TemplateEngine.Resolve(body.Value<string>() ?? body.ToString(), context);
                return new StringContent(text, Encoding.UTF8, "text/plain");
            }

            case BodyType.Form:
            {
                var fields = (body as JObject) ?? throw new InvalidOperationException(
                    "BodyType 'Form' requires Body to be a JSON object of key/value pairs.");
                var pairs = fields.Properties()
                    .Select(p => new KeyValuePair<string, string>(
                        p.Name,
                        TemplateEngine.Resolve(p.Value.Value<string>() ?? p.Value.ToString(), context)));
                return new FormUrlEncodedContent(pairs);
            }

            case BodyType.Multipart:
            {
                var fields = (body as JObject) ?? throw new InvalidOperationException(
                    "BodyType 'Multipart' requires Body to be a JSON object.");
                var multipart = new MultipartFormDataContent();
                foreach (var prop in fields.Properties())
                {
                    var value = TemplateEngine.Resolve(
                        prop.Value.Value<string>() ?? prop.Value.ToString(), context);

                    // Convention: values starting with "base64:" are treated as binary file parts
                    if (value.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
                    {
                        var base64 = value["base64:".Length..];
                        var bytes = Convert.FromBase64String(base64);
                        multipart.Add(new ByteArrayContent(bytes), prop.Name, prop.Name);
                    }
                    else
                    {
                        multipart.Add(new StringContent(value), prop.Name);
                    }
                }
                return multipart;
            }

            case BodyType.Binary:
            {
                var base64 = TemplateEngine.Resolve(body.Value<string>() ?? body.ToString(), context);
                var bytes = Convert.FromBase64String(base64);
                return new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream") }
                };
            }

            default:
                throw new NotSupportedException($"BodyType '{bodyType}' is not supported.");
        }
    }

    private async Task<JToken> ExecuteParallelAsync(
        ParallelStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
    {
        // Each branch gets its own results list to avoid race conditions on the shared list.
        // Context reads are safe (branches only read, not write to steps/variables during parallel execution).
        var branchTasks = step.Steps.Select(async branch =>
        {
            var branchResults = new List<StepRunResult>();
            var started = DateTimeOffset.UtcNow;
            try
            {
                var output = await ExecuteStepAsync(branch, context, branchResults, ct);
                return (branch.Id, Status: RunStatus.Succeeded, Output: output,
                        Started: started, Completed: DateTimeOffset.UtcNow,
                        Results: branchResults, Error: (string?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (branch.Id, Status: RunStatus.Failed, Output: (JToken?)null,
                        Started: started, Completed: DateTimeOffset.UtcNow,
                        Results: branchResults, Error: ex.Message);
            }
        }).ToList();

        var branchOutcomes = await Task.WhenAll(branchTasks);

        // Merge all branch results into the shared context and results list (now single-threaded)
        var branches = new JObject();
        var anyFailed = false;

        foreach (var outcome in branchOutcomes)
        {
            branches[outcome.Id] = new JObject
            {
                ["status"] = outcome.Status.ToString().ToLowerInvariant(),
                ["output"] = outcome.Output ?? JValue.CreateNull()
            };

            context["steps"]![outcome.Id] = new JObject
            {
                ["status"] = outcome.Status == RunStatus.Succeeded ? "succeeded" : "failed",
                ["output"] = outcome.Output ?? JValue.CreateNull()
            };

            foreach (var r in outcome.Results)
                results.Add(r);

            results.Add(new StepRunResult(
                outcome.Id,
                outcome.Status,
                outcome.Started,
                outcome.Completed,
                outcome.Output));

            if (outcome.Status == RunStatus.Failed)
                anyFailed = true;
        }

        if (anyFailed && !step.ContinueOnError)
            throw new InvalidOperationException(
                $"One or more branches of parallel step '{step.Id}' failed.");

        return new JObject
        {
            ["branches"] = branches,
            ["allSucceeded"] = !anyFailed
        };
    }

    private static async Task<JToken> ExecuteDelayAsync(DelayStep step, CancellationToken ct) { await Task.Delay(step.Milliseconds, ct); return JValue.CreateNull(); }
    private static JToken ExecuteScript(ScriptStep step, JObject context)
    {
        var variables = (JObject)context["variables"]!;
        foreach (var assignment in step.Assign) variables[assignment.Key] = TemplateEngine.Resolve(assignment.Value, context);
        return variables.DeepClone();
    }
    private async Task<JToken> ExecuteConditionAsync(ConditionStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
    {
        // Resolve template placeholders in the expression first
        var resolvedExpression = TemplateEngine.Resolve(step.Expression, context);

        // Evaluate the resolved expression as JavaScript
        var passed = JavaScriptExpressionEvaluator.EvaluateAsBoolean(resolvedExpression, context);

        // Execute the appropriate branch
        await ExecuteStepsAsync(passed ? step.Then : step.Else ?? [], context, results, ct);

        return new JObject { ["matched"] = passed };
    }
    private async Task<JToken> ExecuteLoopAsync(LoopStep step, JObject context, List<StepRunResult> results, CancellationToken ct)
    {
        var items = context.SelectToken(step.ItemsExpression, false) as JArray ?? throw new InvalidOperationException("Loop expression must resolve to an array.");
        if (items.Count > step.MaxIterations) throw new InvalidOperationException("Loop iteration limit exceeded.");
        var variables = (JObject)context["variables"]!;
        for (var i=0; i<items.Count; i++) { variables[step.ItemVariable] = items[i]!.DeepClone(); variables[$"{step.ItemVariable}Index"] = i; await ExecuteStepsAsync(step.Steps, context, results, ct); }
        return new JObject { ["iterations"] = items.Count };
    }
}
