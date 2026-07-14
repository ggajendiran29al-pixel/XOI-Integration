using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Microsoft.Extensions.Logging;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XOI_Integration.XOiRepository.XOiTokenProvider;

namespace XOI_Integration.XOiRepository.Provider
{
    public class XOiAPIConnectionClient
    {
        // ✅ Nested handler class
        private class XOiAuthHandler : DelegatingHandler
        {
            private readonly XOiAuthentificationToken _tokenProvider;
            private readonly ILogger _logger;

            public XOiAuthHandler(XOiAuthentificationToken tokenProvider, ILogger logger = null)
            {
                _tokenProvider = tokenProvider;
                _logger = logger; // assign
                InnerHandler = new HttpClientHandler();
            }


            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var start = DateTime.UtcNow;
                var token = await _tokenProvider.GetAuthTokenAsync();

                request.Headers.Remove("Authorization");
                request.Headers.Add("Authorization", token.Token);

                var response = await base.SendAsync(request, cancellationToken);
                var duration = DateTime.UtcNow - start;
                //log on 27th for graph ql 
                _logger?.LogInformation("GraphQL call to {RequestUri} completed. Status={StatusCode} DurationMs={Duration}",
                    request.RequestUri,
                    response.StatusCode,
                    duration.TotalMilliseconds
                );

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    //log on 27th for invalidate token from graph ql
                    _logger?.LogWarning("GraphQL call returned 401 Unauthorized. Token invalidated.");

                return response;
            }
        }

        private static readonly Lazy<GraphQLHttpClient> _lazyGraphQLClient = new Lazy<GraphQLHttpClient>(() =>
        {
            var graphQlUrl = Environment.GetEnvironmentVariable("XOiGrahpQLURL", EnvironmentVariableTarget.Process);

            // ✅ single provider instance used by handler (cache is static too)
            var tokenProvider = new XOiAuthentificationToken();

            // ✅ thread-safe: auth header attached per request, not DefaultRequestHeaders
            var httpClient = new HttpClient(new XOiAuthHandler(tokenProvider,null))
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var options = new GraphQLHttpClientOptions
            {
                EndPoint = new Uri(graphQlUrl)
            };

            return new GraphQLHttpClient(options, new NewtonsoftJsonSerializer(), httpClient);
        });

        public static GraphQLHttpClient Instance => _lazyGraphQLClient.Value;
    }
}