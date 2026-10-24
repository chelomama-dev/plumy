using System.Net;
using System.Text;
using Azure.AI.DocumentIntelligence;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Plumy.Infrastructure.Tests.Extraction;

/// <summary>
/// Plays the Document Intelligence REST API so the real SDK runs end to end, polling included: the analyze request
/// answers 202 with an Operation-Location, and polling that URL answers with <see cref="PollResponse"/>.
/// </summary>
internal sealed class FakeDocumentIntelligence : HttpMessageHandler
{
    public static readonly Uri Endpoint = new("https://di-test.cognitiveservices.azure.com/");
    private static readonly Uri OperationUrl = new(Endpoint,
        "documentintelligence/documentModels/prebuilt-invoice/analyzeResults/op-1?api-version=2024-11-30");

    /// <summary>What the analyze request gets; by default 202, accepted.</summary>
    public Func<HttpResponseMessage> AnalyzeResponse { get; set; } = () => new HttpResponseMessage(HttpStatusCode.Accepted);

    /// <summary>What polling gets: by default a finished analysis with no documents.</summary>
    public Func<HttpResponseMessage> PollResponse { get; set; } = () => Succeeded("[]");

    public HttpRequestMessage? AnalyzeRequest { get; private set; }

    public DocumentIntelligenceClient CreateClient() =>
        new(Endpoint, new StaticToken(), new DocumentIntelligenceClientOptions
        {
            Transport = new HttpClientTransport(new HttpClient(this)),
            // Each test decides the answer; the SDK's own retries would only slow the failing ones down
            Retry = { MaxRetries = 0 }
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post)
        {
            AnalyzeRequest = request;
            var response = AnalyzeResponse();
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                response.Headers.Add("Operation-Location", OperationUrl.ToString());
                // Poll straight away instead of after the default second
                response.Headers.Add("Retry-After", "0");
            }

            return Task.FromResult(response);
        }

        return Task.FromResult(PollResponse());
    }

    /// <summary>A finished analysis with these documents (a JSON array).</summary>
    public static HttpResponseMessage Succeeded(string documentsJson) => Json(HttpStatusCode.OK, $$"""
        {
          "status": "succeeded",
          "createdDateTime": "2026-10-24T10:00:00Z",
          "lastUpdatedDateTime": "2026-10-24T10:00:03Z",
          "analyzeResult": {
            "apiVersion": "2024-11-30",
            "modelId": "prebuilt-invoice",
            "stringIndexType": "textElements",
            "content": "",
            "pages": [],
            "documents": {{documentsJson}}
          }
        }
        """);

    /// <summary>The documents of a file with one invoice with these fields (a JSON object).</summary>
    public static string Invoice(string fieldsJson) => $"[{Document(fieldsJson)}]";

    /// <summary>One invoice document with these fields.</summary>
    public static string Document(string fieldsJson) =>
        $$"""{ "docType": "invoice", "confidence": 1, "spans": [], "fields": {{fieldsJson}} }""";

    public static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        Json(status, $$"""{ "error": { "code": "{{code}}", "message": "Test error" } }""");

    /// <summary>The analysis ran and failed: the polling request itself succeeds (200).</summary>
    public static HttpResponseMessage FailedAnalysis(string code) => Json(HttpStatusCode.OK, $$"""
        {
          "status": "failed",
          "createdDateTime": "2026-10-24T10:00:00Z",
          "lastUpdatedDateTime": "2026-10-24T10:00:03Z",
          "error": { "code": "{{code}}", "message": "Test error" }
        }
        """);

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StaticToken : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Token;

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) => new(Token);
    }
}
