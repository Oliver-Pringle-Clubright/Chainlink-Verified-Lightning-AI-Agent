using System.Security.Cryptography.X509Certificates;

namespace LightningAgent.Lightning;

/// <summary>
/// Static helper that creates an <see cref="HttpClientHandler"/> configured for LND's TLS certificate.
/// </summary>
internal static class LndTlsCertHandler
{
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
        else
        {
            // Dev / self-signed mode: trust all certificates
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return handler;
    }
}
