

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics.Models;
using Microsoft.Rest;

namespace HaBot.Client
{
    public class TextSentimentClient
    {
        private readonly string _serviceUrl;
        private readonly TextAnalyticsClient _client;

        public TextSentimentClient(string serviceUrl, string subscriptionKey)
        {
            _serviceUrl = serviceUrl;

            _client = new TextAnalyticsClient(new ApiKeyServiceClientCredentials(subscriptionKey))
            {
                Endpoint = serviceUrl
            };
        }

        public async Task<string> GetSentimentFromText(string textToAnalyze, string language = "en")
        {
            var sentimentResult = await _client.SentimentAsync(
                new MultiLanguageBatchInput(
                    new List<MultiLanguageInput>(){
                        new MultiLanguageInput(language, "0", textToAnalyze),
                    })).ConfigureAwait(false);

            
            var result = sentimentResult.Documents.First();

            return $"{String.Format("{0:00.00}", result.Score * 100)}% positive";
        }

        /// <summary>
        /// Container for subscription credentials. Make sure to enter your valid key.
        /// </summary>
        private class ApiKeyServiceClientCredentials : ServiceClientCredentials
        {
            private readonly string _subscriptionKey;

            public ApiKeyServiceClientCredentials(string subscriptionKey)
            {
                _subscriptionKey = subscriptionKey;
            }

            public override Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
                return base.ProcessHttpRequestAsync(request, cancellationToken);
            }
        }
    }
}