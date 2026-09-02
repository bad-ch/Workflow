using Newtonsoft.Json.Linq;

namespace Workflow.Application.Interfaces;

/// <summary>
/// Probes a remote host and returns TLS certificate metadata as a
/// <see cref="JObject"/> so workflow steps can reference cert fields
/// using the standard <c>{{steps.x.output.certificate.expiresUtc}}</c> syntax.
/// Returns <c>null</c> when the URL is not HTTPS or the probe fails.
/// </summary>
public interface ICertificateInspector
{
    Task<JObject?> InspectAsync(Uri uri, CancellationToken cancellationToken);
}
