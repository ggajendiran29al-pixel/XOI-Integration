using GraphQL;
using GraphQL.Client.Http;
using System;
using System.Threading.Tasks;

namespace XOI_Integration.XOiRepository.Provider
{
    public class XOiAPI
    {
        private readonly GraphQLHttpClient _graphQlClient;

        public XOiAPI()
        {
            _graphQlClient = XOiAPIConnectionClient.Instance;
        }

        public async Task<GraphQLResponse<T>> SendRequestAsync<T>(string query, object variables)
        {
            var graphQlRequest = new GraphQLRequest
            {
                Query = query,
                Variables = variables
            };

            var graphQlResponse = await _graphQlClient.SendQueryAsync<T>(graphQlRequest);

            if (graphQlResponse == null)
                throw new Exception("GraphQL response is null.");

            return graphQlResponse;
        }
    }
}