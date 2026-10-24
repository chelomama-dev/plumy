using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;

namespace Plumy.Infrastructure.Tests.Classification;

/// <summary>
/// Plays the OpenAI v1 chat completions API that Foundry serves, so the real SDK runs end to end: the request it
/// builds is recorded, and <see cref="Response"/> is what it gets back.
/// </summary>
internal sealed class FakeChatCompletions : HttpMessageHandler
{
    public const string Deployment = "gpt-5.4-mini";

    public Func<HttpResponseMessage> Response { get; set; } = () => Answer("""{ "code": "628", "reason": "Luz." }""");

    public HttpRequestMessage? Request { get; private set; }
    public JsonDocument? RequestBody { get; private set; }

    public ChatClient CreateClient() =>
        new(Deployment, new ApiKeyCredential("test-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://aif-test.openai.azure.com/openai/v1/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(this)),
            // Each test decides the answer; the SDK's own retries would only slow the failing ones down
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        RequestBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        return Response();
    }

    /// <summary>A finished completion whose message is this content (the model's JSON, as a string).</summary>
    public static HttpResponseMessage Answer(string content, string finishReason = "stop", string? refusal = null) =>
        Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            id = "chatcmpl-1",
            @object = "chat.completion",
            created = 1761300000,
            model = Deployment,
            choices = new[]
            {
                new
                {
                    index = 0,
                    finish_reason = finishReason,
                    message = new { role = "assistant", content, refusal }
                }
            },
            usage = new { prompt_tokens = 10, completion_tokens = 5, total_tokens = 15 }
        }));

    public static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        Json(status, $$"""{ "error": { "code": "{{code}}", "message": "Test error" } }""");

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
