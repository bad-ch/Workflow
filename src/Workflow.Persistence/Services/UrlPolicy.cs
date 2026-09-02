using System.Net; using Microsoft.Extensions.Options; using Workflow.Application.Interfaces; using Workflow.Persistence.Options;
namespace Workflow.Persistence.Services;
public sealed class UrlPolicy(IOptions<WorkflowSecurityOptions> options) : IUrlPolicy
{
 public async Task ValidateAsync(Uri uri, CancellationToken cancellationToken)
 {
  var cfg = options.Value; if (uri.Scheme != Uri.UriSchemeHttps && !(cfg.AllowHttp && uri.Scheme == Uri.UriSchemeHttp)) throw new InvalidOperationException("URL scheme is not allowed.");
  if (cfg.AllowedHosts.Length == 0 || !cfg.AllowedHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Destination host is not allow-listed.");
  var addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken); if (addresses.Length == 0 || addresses.Any(IsPrivate)) throw new InvalidOperationException("Destination resolved to a disallowed address.");
 }
 private static bool IsPrivate(IPAddress ip){if(IPAddress.IsLoopback(ip))return true;if(ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork){var b=ip.GetAddressBytes();return b[0]==10||b[0]==127||(b[0]==169&&b[1]==254)||(b[0]==172&&b[1] is >=16 and <=31)||(b[0]==192&&b[1]==168);}return ip.IsIPv6LinkLocal||ip.IsIPv6SiteLocal||ip.Equals(IPAddress.IPv6Loopback);}
}
