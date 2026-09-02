using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Workflow.Application.Interfaces;
using Workflow.Application.Models;

namespace Workflow.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController(
    IWorkflowRepository repository,
    IWorkflowQueue queue) : ControllerBase
{
    [HttpPost("{workflowId:guid}/{key}")]
    public async Task<IActionResult> Invoke(
        Guid workflowId,
        string key,
        [FromBody] JObject? input,
        CancellationToken cancellationToken)
    {
        var workflow = await repository.GetAsync(workflowId, cancellationToken);
        if (workflow is null || workflow.Trigger.Type != TriggerType.Webhook)
            return NotFound();

        if (string.IsNullOrEmpty(workflow.Trigger.WebhookKey) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(workflow.Trigger.WebhookKey),
                System.Text.Encoding.UTF8.GetBytes(key)))
            return NotFound();

        await queue.EnqueueAsync(
            new(Guid.NewGuid(), workflowId, input ?? new JObject()),
            cancellationToken);

        return Accepted();
    }
}
