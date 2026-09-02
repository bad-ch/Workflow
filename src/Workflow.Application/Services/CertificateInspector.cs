using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json.Linq;
using Workflow.Application.Interfaces;

namespace Workflow.Application.Services;

/// <summary>
/// Opens a dedicated TLS connection to the target host and extracts
/// certificate metadata into a <see cref="JObject"/>.
/// </summary>
public sealed class CertificateInspector : ICertificateInspector
{
    public async Task<JObject?> InspectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return null;

        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 443;

        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, cancellationToken);

            X509Certificate2? cert = null;

            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);

            // CA5359: intentionally accepts any cert — this connection is read-only
            // (we only retrieve metadata; no sensitive data is sent over it).
            #pragma warning disable CA5359
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = host,
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        if (certificate is X509Certificate2 c2)
                            cert = c2;
                        else if (certificate is not null)
                            cert = new X509Certificate2(certificate);
                        return true;
                    }
                },
                cancellationToken);
            #pragma warning restore CA5359

            if (cert is null)
                return null;

            return BuildCertJson(cert, ssl.NegotiatedCipherSuite.ToString());
        }
        catch
        {
            return null; // non-fatal — cert data simply won't be present in the step output
        }
    }

    private static JObject BuildCertJson(X509Certificate2 cert, string? cipherSuite)
    {
        var sans = new JArray();
        var sanExtension = cert.Extensions
            .OfType<X509SubjectAlternativeNameExtension>()
            .FirstOrDefault();

        if (sanExtension is not null)
            foreach (var name in sanExtension.EnumerateDnsNames())
                sans.Add(name);

        var daysRemaining = (cert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;

        return new JObject
        {
            ["subject"]         = cert.Subject,
            ["issuer"]          = cert.Issuer,
            ["serialNumber"]    = cert.SerialNumber,
            ["thumbprint"]      = cert.Thumbprint,
            ["notBeforeUtc"]    = cert.NotBefore.ToUniversalTime().ToString("O"),
            ["expiresUtc"]      = cert.NotAfter.ToUniversalTime().ToString("O"),
            ["daysUntilExpiry"] = Math.Floor(daysRemaining),
            ["isExpired"]       = daysRemaining < 0,
            ["expiresSoon"]     = daysRemaining is >= 0 and <= 30,
            ["signatureAlgorithm"] = cert.SignatureAlgorithm.FriendlyName,
            ["publicKeyAlgorithm"] = cert.PublicKey.Oid.FriendlyName,
            ["keySize"]         = GetKeySize(cert),
            ["subjectAltNames"] = sans,
            ["cipherSuite"]     = cipherSuite,
            ["parsedSubject"]   = ParseDistinguishedName(cert.Subject),
            ["parsedIssuer"]    = ParseDistinguishedName(cert.Issuer)
        };
    }

    /// <summary>Parses a DN string into a flat object: CN, O, OU, C, ST, L.</summary>
    private static JObject ParseDistinguishedName(string dn)
    {
        var result = new JObject();
        foreach (var part in dn.Split(',', StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) continue;
            var key   = part[..eq].Trim().ToUpperInvariant();
            var value = part[(eq + 1)..].Trim();
            result[key] = value;
        }
        return result;
    }

    private static int? GetKeySize(X509Certificate2 cert)
    {
        try
        {
            if (cert.GetRSAPublicKey() is RSA rsa)          return rsa.KeySize;
            if (cert.GetECDsaPublicKey() is ECDsa ecdsa)    return ecdsa.KeySize;
            if (cert.GetDSAPublicKey() is DSA dsa)          return dsa.KeySize;
        }
        catch { /* ignore */ }
        return null;
    }
}
