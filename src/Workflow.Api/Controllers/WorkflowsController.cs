using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;
using Workflow.Application.Services;

namespace Workflow.Api.Controllers;

[ApiController]
[Route("api/workflows")]
public sealed class WorkflowsController(
    IWorkflowRepository repository,
    IWorkflowQueue queue) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowResponse>>> GetAll(CancellationToken ct)
        => Ok((await repository.GetAllAsync(ct)).Select(Map));

    [HttpGet("{id:guid}", Name = "GetWorkflow")]
    public async Task<ActionResult<WorkflowResponse>> Get(Guid id, CancellationToken ct)
    {
        var workflow = await repository.GetAsync(id, ct);
        return workflow is null ? NotFound() : Ok(Map(workflow));
    }

    [HttpPost]
    public async Task<ActionResult<WorkflowResponse>> Create(
        [FromBody] CreateWorkflowRequest request,
        CancellationToken ct)
    {
        var errors = WorkflowValidator.Validate(request);
        if (errors.Count > 0)
            return ValidationProblem(ToValidationProblem(errors));

        var now = DateTimeOffset.UtcNow;
        var workflow = new WorkflowDefinition(
            Guid.NewGuid(),
            request.Name,
            request.Enabled,
            request.Trigger,
            request.Schedule,
            request.Steps,
            now,
            now);

        await repository.SaveAsync(workflow, ct);

        return CreatedAtRoute("GetWorkflow", new { id = workflow.Id }, Map(workflow));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateWorkflowRequest request,
        CancellationToken ct)
    {
        var errors = WorkflowValidator.Validate(request);
        if (errors.Count > 0)
            return ValidationProblem(ToValidationProblem(errors));

        var existing = await repository.GetAsync(id, ct);
        if (existing is null)
            return NotFound();

        await repository.SaveAsync(existing with
        {
            Name = request.Name,
            Enabled = request.Enabled,
            Trigger = request.Trigger,
            Schedule = request.Schedule,
            Steps = request.Steps,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, ct);

        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => await repository.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/runs")]
    public async Task<IActionResult> Run(
        Guid id,
        [FromBody] RunWorkflowRequest request,
        CancellationToken ct)
    {
        if (await repository.GetAsync(id, ct) is null)
            return NotFound();

        var runId = Guid.NewGuid();
        await queue.EnqueueAsync(new(runId, id, request.Input ?? new JObject()), ct);

        return AcceptedAtAction(
            nameof(RunsController.Get),
            "Runs",
            new { id = runId },
            new { runId, workflowId = id, status = "queued" });
    }

    private static WorkflowResponse Map(WorkflowDefinition w)
        => new(w.Id, w.Name, w.Enabled, w.Trigger, w.Schedule, w.Steps, w.CreatedUtc, w.UpdatedUtc);

    private static ValidationProblemDetails ToValidationProblem(
        IReadOnlyDictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails();
        foreach (var (key, messages) in errors)
            details.Errors[key] = messages;
        return details;
    }
}
