using System.Security.Cryptography.X509Certificates;

namespace LightningAgentMarketPlace.Lightning;

/// <summary>
/// Static helper that creates an <see cref="HttpClientHandler"/> configured for LND's TLS certificate.
/// </summary>
public static class LndTlsCertHandler
{
    /// <summary>
    /// When true, allows DangerousAcceptAnyServerCertificateValidator as a fallback
    /// when no TLS cert path is provided. Must be explicitly set at startup.
    /// </summary>
    public static bool AllowInsecureDevelopmentMode { get; set; }

    public static HttpClientHandler CreateHttpClientHandler(string tlsCertPath)
    {
        var handler = new HttpClientHandler();

        if (!string.IsNullOrEmpty(tlsCertPath) && File.Exists(tlsCertPath))
        {
            var cert = X509CertificateLoader.LoadCertificateFromFile(tlsCertPath);
            handler.ClientCertificates.Add(cert);

            handler.ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) =>
            {
                if (certificate is null) return false;

                // Trust the specific LND TLS certificate
                return certificate.GetCertHashString() == cert.GetCertHashString()
                    || errors == System.Net.Security.SslPolicyErrors.None;
            };
        }
        else if (AllowInsecureDevelopmentMode)
        {
            // Dev-only: trust all certificates. This is gated behind an explicit opt-in
            // that Program.cs only sets when ASPNETCORE_ENVIRONMENT=Development.
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        else
        {
            throw new InvalidOperationException(
                "Lightning:TlsCertPath is not configured or the file does not exist. " +
                "TLS certificate validation is required in non-development environments. " +
                "Provide the LND TLS certificate path in configuration.");
        }

        return handler;
    }
}
