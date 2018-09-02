using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bing.Speech;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HaBot.Client
{
    public class SpeechToTextClient
    {
        private readonly Uri _serviceUrl;
        private readonly string _subscriptionKey;

        /// <summary>
        /// Cancellation token used to stop sending the audio.
        /// </summary>
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        public SpeechToTextClient(Uri serviceUrl, string subscriptionKey)
        {
            _serviceUrl = serviceUrl;
            _subscriptionKey = subscriptionKey;
        }

        /// <summary>
        /// Sends a speech recognition request to the speech service
        /// </summary>
        /// <param name="audioFile">The audio file.</param>
        /// <param name="locale">The locale.</param>
        /// <returns>
        /// A task
        /// </returns>
        public async Task<string> TranslateToText(string audioFile, string locale = "en-GB")
        {
            var preferences = new Preferences(locale, _serviceUrl, new CognitiveServicesAuthorizationProvider(_subscriptionKey));

            // Create a a speech client
            using (var speechClient = new SpeechClient(preferences))
            {
                speechClient.SubscribeToPartialResult(this.OnPartialResult);
                speechClient.SubscribeToRecognitionResult(this.OnRecognitionResult);

                try
                {
                    // create an audio content and pass it a stream.
                    using (var downloadStream = new WebClient())
                    using (var audio = new MemoryStream(downloadStream.DownloadData(audioFile)))
                    {
                        var deviceMetadata = new DeviceMetadata(DeviceType.Near, DeviceFamily.Desktop,
                            NetworkType.Ethernet, OsName.Windows, "1607", "Dell", "T3600");
                        var applicationMetadata = new ApplicationMetadata("SampleApp", "1.0.0");
                        var requestMetadata = new RequestMetadata(Guid.NewGuid(), deviceMetadata, applicationMetadata,
                            "SampleAppService");

                        await speechClient.RecognizeAsync(new SpeechInput(audio, requestMetadata), this.cts.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    if (e is PlatformNotSupportedException)
                    {
                       return await TranslateToTextFallback(audioFile).ConfigureAwait(false); //fallback for when websockets are not supported
                    }
                }
            }

            return string.Empty;
        }


        private async Task<string> TranslateToTextFallback(string audioFile)
        {
            HttpWebRequest request = null;
            request = (HttpWebRequest) HttpWebRequest.Create(new Uri(
                "https://speech.platform.bing.com/speech/recognition/interactive/cognitiveservices/v1?language=en-US&format=detailed"));
            request.SendChunked = true;
            request.Accept = @"application/json;text/xml";
            request.Method = "POST";
            request.ProtocolVersion = HttpVersion.Version11;
            request.ContentType = @"audio/wav; codec=audio/pcm; samplerate=16000";
            request.Headers["Ocp-Apim-Subscription-Key"] = _subscriptionKey;

            // Send an audio file by 1024 byte chunks
            using (var downloadStream = new WebClient())
            using (var audio = new MemoryStream(downloadStream.DownloadData(audioFile)))
            {

                /*
                * Open a request stream and write 1024 byte chunks in the stream one at a time.
                */
                byte[] buffer = null;
                int bytesRead = 0;
                using (Stream requestStream = request.GetRequestStream())
                {
                    /*
                    * Read 1024 raw bytes from the input audio file.
                    */
                    buffer = new Byte[checked((uint)Math.Min(1024, (int)audio.Length))];
                    while ((bytesRead = audio.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        requestStream.Write(buffer, 0, bytesRead);
                    }

                    // Flush
                    requestStream.Flush();
                }
            }
           
            using (WebResponse response = await request.GetResponseAsync().ConfigureAwait(false))
            {
                var statusCode = ((HttpWebResponse) response).StatusCode;
                if (statusCode == HttpStatusCode.OK)
                {
                    using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                    {
                        var responseBody = sr.ReadToEnd();
                        var data = JsonConvert.DeserializeObject<BingResponse>(responseBody);
                        return data.NBest.First().Display;
                    }
                }
                else
                {
                    return "Could not recognize";
                }
            }
        }


        /// <summary>
            /// Invoked when the speech client receives a partial recognition hypothesis from the server.
            /// </summary>
            /// <param name="args">The partial response recognition result.</param>
            /// <returns>
            /// A task
            /// </returns>
            public Task OnPartialResult(RecognitionPartialResult args)
        {
            Console.WriteLine("--- Partial result received by OnPartialResult ---");

            // Print the partial response recognition hypothesis.
            Console.WriteLine(args.DisplayText);

            Console.WriteLine();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Invoked when the speech client receives a phrase recognition result(s) from the server.
        /// </summary>
        /// <param name="args">The recognition result.</param>
        /// <returns>
        /// A task
        /// </returns>
        public Task OnRecognitionResult(RecognitionResult args)
        {
            var response = args;
            Console.WriteLine();

            Console.WriteLine("--- Phrase result received by OnRecognitionResult ---");

            // Print the recognition status.
            Console.WriteLine("***** Phrase Recognition Status = [{0}] ***", response.RecognitionStatus);
            if (response.Phrases != null)
            {
                foreach (var result in response.Phrases)
                {
                    // Print the recognition phrase display text.
                    Console.WriteLine("{0} (Confidence:{1})", result.DisplayText, result.Confidence);
                }
            }

            Console.WriteLine();
            return Task.CompletedTask;
        }
        private class BingResponse
        {
            public string RecognitionStatus { get; set; }
            public int Offset { get; set; }
            public int Duration { get; set; }
            public List<NBest> NBest { get; set; }

        }

        private class NBest
        {
            public string Confidence { get; set; }
            public string Lexical { get; set; }
            public string ITN { get; set; }
            public string MaskedITN { get; set; }
            public string Display { get; set; }
        }
    }
    /// <summary>
    /// Cognitive Services Authorization Provider.
    /// </summary>
    public sealed class CognitiveServicesAuthorizationProvider : IAuthorizationProvider
    {
        /// <summary>
        /// The fetch token URI
        /// </summary>
        private const string FetchTokenUri = "https://api.cognitive.microsoft.com/sts/v1.0";

        /// <summary>
        /// The subscription key
        /// </summary>
        private readonly string subscriptionKey;

        /// <summary>
        /// Initializes a new instance of the <see cref="CognitiveServicesAuthorizationProvider" /> class.
        /// </summary>
        /// <param name="subscriptionKey">The subscription identifier.</param>
        public CognitiveServicesAuthorizationProvider(string subscriptionKey)
        {
            if (subscriptionKey == null)
            {
                throw new ArgumentNullException(nameof(subscriptionKey));
            }

            if (string.IsNullOrWhiteSpace(subscriptionKey))
            {
                throw new ArgumentException(nameof(subscriptionKey));
            }

            this.subscriptionKey = subscriptionKey;
        }

        /// <summary>
        /// Gets the authorization token asynchronously.
        /// </summary>
        /// <returns>
        /// A task that represents the asynchronous read operation. The value of the string parameter contains the next the authorization token.
        /// </returns>
        /// <remarks>
        /// This method should always return a valid authorization token at the time it is called.
        /// </remarks>
        public Task<string> GetAuthorizationTokenAsync()
        {
            return FetchToken(FetchTokenUri, this.subscriptionKey);
        }

        /// <summary>
        /// Fetches the token.
        /// </summary>
        /// <param name="fetchUri">The fetch URI.</param>
        /// <param name="subscriptionKey">The subscription key.</param>
        /// <returns>An access token.</returns>
        private static async Task<string> FetchToken(string fetchUri, string subscriptionKey)
        {
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);
                var uriBuilder = new UriBuilder(fetchUri);
                uriBuilder.Path += "/issueToken";

                using (var result = await client.PostAsync(uriBuilder.Uri.AbsoluteUri, null).ConfigureAwait(false))
                {
                    return await result.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }
    }

}
