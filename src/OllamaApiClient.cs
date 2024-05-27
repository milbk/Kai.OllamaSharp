using OllamaSharp.Models;
using OllamaSharp.Streamer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using OllamaSharp.Models.Chat;
using System.Threading;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Message = OllamaSharp.Models.Chat.Message;

namespace OllamaSharp
{
    // https://github.com/jmorganca/ollama/blob/main/docs/api.md
    public class OllamaApiClient : IOllamaApiClient
    {
        public class Configuration
        {
            public Uri Uri { get; set; }

            public string Model { get; set; }
        }

        private readonly HttpClient _client;

        public Configuration Config { get; }

        public string SelectedModel { get; set; }

        public OllamaApiClient(string uriString, string defaultModel = "")
            : this(new Uri(uriString), defaultModel)
        {
        }

        public OllamaApiClient(Uri uri, string defaultModel = "")
            : this(new Configuration { Uri = uri, Model = defaultModel })
        {
        }

        public OllamaApiClient(Configuration config)
            : this(
                new HttpClient()
                {
                    BaseAddress = config.Uri, Timeout = TimeSpan.FromMinutes(15),
                    DefaultRequestHeaders = {{"Access-Control-Allow-Private-Network", "true"}}
                }, config.Model)
        {
        }

        public OllamaApiClient(HttpClient client, string defaultModel = "")
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            SelectedModel = defaultModel;
        }

        public async Task CreateModel(CreateModelRequest request, IResponseStreamer<CreateStatus> streamer, CancellationToken cancellationToken = default)
        {
            await StreamPostAsync("/api/create", request, streamer, cancellationToken);
        }

        public async Task DeleteModel(string model, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
            {
                Content = new StringContent(JsonSerializer.Serialize(new DeleteModelRequest { Name = model }), Encoding.UTF8, "application/json")
            };

            using var response = await _client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task<IEnumerable<Model>> ListLocalModels(CancellationToken cancellationToken = default)
        {
            var data = await GetAsync<ListModelsResponse>("/api/tags", cancellationToken);
            return data.Models;
        }

        public async Task<ShowModelResponse> ShowModelInformation(string model, CancellationToken cancellationToken = default)
        {
            return await PostAsync<ShowModelRequest, ShowModelResponse>("/api/show", new ShowModelRequest { Name = model }, cancellationToken);
        }

        public async Task CopyModel(CopyModelRequest request, CancellationToken cancellationToken = default)
        {
            await PostAsync("/api/copy", request, cancellationToken);
        }

        public async Task PullModel(PullModelRequest request, IResponseStreamer<PullStatus> streamer, CancellationToken cancellationToken = default)
        {
            await StreamPostAsync("/api/pull", request, streamer, cancellationToken);
        }

        public async Task PushModel(PushRequest request, IResponseStreamer<PushStatus> streamer, CancellationToken cancellationToken = default)
        {
            await StreamPostAsync("/api/push", request, streamer, cancellationToken);
        }

        public async Task<GenerateEmbeddingResponse> GenerateEmbeddings(GenerateEmbeddingRequest request, CancellationToken cancellationToken = default)
        {
            return await PostAsync<GenerateEmbeddingRequest, GenerateEmbeddingResponse>("/api/embeddings", request, cancellationToken);
        }

        public async Task<ConversationContext> StreamCompletion(GenerateCompletionRequest request, IResponseStreamer<GenerateCompletionResponseStream> streamer, CancellationToken cancellationToken = default)
        {
            return await GenerateCompletion(request, streamer, cancellationToken);
        }

        public async Task<ConversationContextWithResponse> GetCompletion(GenerateCompletionRequest request, CancellationToken cancellationToken = default)
        {
            var builder = new StringBuilder();
            var result = await GenerateCompletion(request, new ActionResponseStreamer<GenerateCompletionResponseStream>(status => builder.Append(status.Response)), cancellationToken);
            return new ConversationContextWithResponse(builder.ToString(), result.Context);
        }

        public async Task<IEnumerable<Message>> SendChat(ChatRequest chatRequest, IResponseStreamer<ChatResponseStream> streamer, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
            {
				Content = new StringContent(JsonSerializer.Serialize(chatRequest), Encoding.UTF8, "application/json")
            };
            request.SetBrowserResponseStreamingEnabled(true);
            var completion = chatRequest.Stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;

            using var response = await _client.SendAsync(request, completion, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await ProcessStreamedChatResponseAsync(chatRequest, response, streamer, cancellationToken);
        }

		public async Task<bool> IsRunning(CancellationToken cancellationToken = default)
		{
			var response = await _client.GetAsync("", cancellationToken); // without route returns "Ollama is running"
			response.EnsureSuccessStatusCode();
			var stringContent = await response.Content.ReadAsStringAsync();
			return !string.IsNullOrWhiteSpace(stringContent);
		}

        private async Task<ConversationContext> GenerateCompletion(GenerateCompletionRequest generateRequest, IResponseStreamer<GenerateCompletionResponseStream> streamer, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/generate")
            {
                Content = new StringContent(JsonSerializer.Serialize(generateRequest), Encoding.UTF8, "application/json")
            };
            request.SetBrowserResponseStreamingEnabled(true);
            var completion = generateRequest.Stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead;

            using var response = await _client.SendAsync(request, completion, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await ProcessStreamedCompletionResponseAsync(response, streamer, cancellationToken);
        }

        private async Task<TResponse> GetAsync<TResponse>(string endpoint, CancellationToken cancellationToken)
        {
            var response = await _client.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<TResponse>(responseBody,
                new JsonSerializerOptions {DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull});
        }

        private async Task PostAsync<TRequest>(string endpoint, TRequest request, CancellationToken cancellationToken)
        {
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(endpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        private async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest request, CancellationToken cancellationToken)
        {
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(endpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<TResponse>(responseBody);
        }

        private async Task StreamPostAsync<TRequest, TResponse>(string endpoint, TRequest requestModel, IResponseStreamer<TResponse> streamer, CancellationToken cancellationToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(requestModel), Encoding.UTF8, "application/json")
            };
            request.SetBrowserResponseStreamingEnabled(true);

            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await ProcessStreamedResponseAsync(response, streamer, cancellationToken);
        }

        private static async Task ProcessStreamedResponseAsync<TLine>(HttpResponseMessage response, IResponseStreamer<TLine> streamer, CancellationToken cancellationToken)
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            //!reader.EndOfStream
            while (!cancellationToken.IsCancellationRequested)
            {
                string line = await reader.ReadLineAsync();
                if (line == null) break;
                var streamedResponse = JsonSerializer.Deserialize<TLine>(line);
                streamer.Stream(streamedResponse);
            }
        }

        private static async Task<ConversationContext> ProcessStreamedCompletionResponseAsync(HttpResponseMessage response, IResponseStreamer<GenerateCompletionResponseStream> streamer, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                //!reader.EndOfStream &&
                while (!cancellationToken.IsCancellationRequested)
                {
                    string line = await reader.ReadLineAsync();
                    if (line == null) break;
                    var streamedResponse = JsonSerializer.Deserialize<GenerateCompletionResponseStream>(line);
                    streamer.Stream(streamedResponse);

                    if (streamedResponse?.Done ?? false)
                    {
                        var doneResponse = JsonSerializer.Deserialize<GenerateCompletionDoneResponseStream>(line);
                        return new ConversationContext(doneResponse.Context);
                    }
                }

                return new ConversationContext(Array.Empty<long>());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new ConversationContext(Array.Empty<long>());
            }
        }

        private static async Task<IEnumerable<Message>> ProcessStreamedChatResponseAsync(ChatRequest chatRequest, HttpResponseMessage response, IResponseStreamer<ChatResponseStream> streamer, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);

                ChatRole? responseRole = null;
                var responseContent = new StringBuilder();

                //!reader.EndOfStream
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;

                    var streamedResponse = JsonSerializer.Deserialize<ChatResponseStream>(line);

                    // keep the streamed content to build the last message
                    // to return the list of messages
                    responseRole ??= streamedResponse?.Message?.Role;
                    responseContent.Append(streamedResponse?.Message?.Content ?? "");

                    streamer.Stream(streamedResponse);

                    if (streamedResponse?.Done ?? false)
                    {
                        var doneResponse = JsonSerializer.Deserialize<ChatDoneResponseStream>(line);
                        var messages = chatRequest.Messages.ToList();
                        messages.Add(new Message(responseRole, responseContent.ToString()));
                        return messages;
                    }
                }

                return Array.Empty<Message>();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return Array.Empty<Message>();
            }
        }
    }

    public record ConversationContext(long[] Context);

    public record ConversationContextWithResponse(string Response, long[] Context) : ConversationContext(Context);
}
