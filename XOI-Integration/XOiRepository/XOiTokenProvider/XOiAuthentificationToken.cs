using Google.Protobuf.WellKnownTypes;
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XOI_Integration.XOiRepository.XOiDataModels;

namespace XOI_Integration.XOiRepository.XOiTokenProvider
{
    public class XOiAuthentificationToken
    {
        // ✅ Reuse one HttpClient (avoid socket exhaustion under load)
        private static readonly HttpClient _httpClient = new HttpClient();

        // ✅ Prevent many concurrent token calls during peak
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private static XOiToken _cachedToken;
        private static DateTime _validUntilUtc = DateTime.MinValue;

        // Since XOiToken has no expiry, assume token TTL.
        // Start with 50 minutes and adjust if XOi confirms actual TTL.
        private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(50);

        // Refresh token early to avoid expiry edge cases
        private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

        public async Task<XOiToken> GetAuthTokenAsync()
        {
           // Added Log on 27th Fast path: return cached token if still valid
            if (_cachedToken != null && DateTime.UtcNow < (_validUntilUtc - RefreshSkew))
                return _cachedToken;

            await _lock.WaitAsync();
            try
            {
                // Added Log on 27th Double-check after lock
                if (_cachedToken != null && DateTime.UtcNow < (_validUntilUtc - RefreshSkew))
                {
                    Console.WriteLine($"[XOiAuthToken] Returning cached token after lock. Valid until {_validUntilUtc} UTC, Time={DateTime.UtcNow}");
                    return _cachedToken;
                }

                string tokenURL = Environment.GetEnvironmentVariable("XOiAPIGetTokenURL", EnvironmentVariableTarget.Process);
                string apiKey = Environment.GetEnvironmentVariable("XOIAPIKey", EnvironmentVariableTarget.Process);
                string apiSecret = Environment.GetEnvironmentVariable("XOiAPISecret", EnvironmentVariableTarget.Process);

                var requestBody = new { api_key = apiKey, api_secret = apiSecret };
                var requestBodyJson = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");

                //var response = await _httpClient.PostAsync(tokenURL, content);
                //log on 27th for Request Token time login
                var requestStart = DateTime.UtcNow;
                var response = await _httpClient.PostAsync(tokenURL, content);
                var requestDuration = DateTime.UtcNow - requestStart;

                Console.WriteLine($"[XOiAuthToken] Token request to {tokenURL} completed. Status={response.StatusCode}, DurationMs={requestDuration.TotalMilliseconds}, Time={DateTime.UtcNow}");
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[XOiAuthToken] Failed to retrieve token from {tokenURL}. Status={response.StatusCode} at {DateTime.UtcNow}");
                    throw new Exception($"Failed to retrieve authentication token. Status code: {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var token = Newtonsoft.Json.JsonConvert.DeserializeObject<XOiToken>(responseContent);

                if (token == null || string.IsNullOrWhiteSpace(token.Token))
                {
                    Console.WriteLine($"[XOiAuthToken] Token endpoint returned empty token from {tokenURL} at {DateTime.UtcNow}");
                    throw new Exception("Token endpoint returned empty token.");
                }   

                _cachedToken = token;
                _validUntilUtc = DateTime.UtcNow.Add(TokenLifetime);

                //Added lg on 27th 
                Console.WriteLine($"[XOiAuthToken] Successfully retrieved token from {tokenURL}. Token valid until {_validUntilUtc} UTC, Time={DateTime.UtcNow}");

                return _cachedToken;
            }
            finally
            {
                _lock.Release();
            }
        }

        // 401, force refresh next time Added Log on 27th 
        public void InvalidateToken()
        {
            Console.WriteLine($"[XOiAuthToken] Token invalidated at {DateTime.UtcNow}");
            _validUntilUtc = DateTime.MinValue;
            _cachedToken = null;
        }
    }
}