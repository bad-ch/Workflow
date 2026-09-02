using Microsoft.AspNetCore.Mvc;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;

namespace Workflow.Api.Controllers;

[ApiController]
[Route("api/runs")]
public sealed class RunsController(IRunRepository repository) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WorkflowRun>> Get(Guid id, CancellationToken cancellationToken)
    {
        var run = await repository.GetAsync(id, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }
}
