using System.Security.Cryptography.X509Certificates;
using Azure.Security.KeyVault.Secrets;
using CPS_API.Models;

namespace CPS_API.Services
{
    public interface ICertificateService
    {
        Task<X509Certificate2?> GetCertificateAsync();
    }

    public class CertificateService : ICertificateService
    {
        private readonly GlobalSettings _globalSettings;
        private readonly SecretClient _secretClient;

        public CertificateService(
            Microsoft.Extensions.Options.IOptions<GlobalSettings> settings,
            SecretClient secretClient)
        {
            _globalSettings = settings.Value;
            _secretClient = secretClient;
        }

        public async Task<X509Certificate2?> GetCertificateAsync()
        {
            var response = await _secretClient.GetSecretAsync(_globalSettings.CertificateName);
            var keyVaultSecret = response?.Value;
            if (keyVaultSecret != null)
            {
                var privateKeyBytes = Convert.FromBase64String(keyVaultSecret.Value);
                return new X509Certificate2(privateKeyBytes);
            }
            return null;
        }
    }
}
