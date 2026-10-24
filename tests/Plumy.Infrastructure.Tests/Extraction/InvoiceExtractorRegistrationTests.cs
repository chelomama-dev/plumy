using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Invoices.Extraction;
using Plumy.Infrastructure.Extraction;

namespace Plumy.Infrastructure.Tests.Extraction;

public sealed class InvoiceExtractorRegistrationTests
{
    [Fact]
    public void Document_intelligence_is_used_with_its_endpoint()
    {
        var extractor = Resolve(new()
        {
            ["InvoiceExtraction:Extractor"] = "DocumentIntelligence",
            ["InvoiceExtraction:DocumentIntelligence:Endpoint"] = "https://di-test.cognitiveservices.azure.com/"
        });

        Assert.IsType<DocumentIntelligenceInvoiceExtractor>(extractor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("di-test.cognitiveservices.azure.com")]
    [InlineData("http://di-test.cognitiveservices.azure.com/")]
    public void Document_intelligence_without_a_valid_endpoint_refuses_to_start(string? endpoint)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(new()
        {
            ["InvoiceExtraction:Extractor"] = "DocumentIntelligence",
            ["InvoiceExtraction:DocumentIntelligence:Endpoint"] = endpoint
        }));

        Assert.Contains("Endpoint", ex.Message);
    }

    [Fact]
    public void The_fake_is_used_only_when_asked_for()
    {
        var extractor = Resolve(new() { ["InvoiceExtraction:Extractor"] = "Fake" });

        Assert.IsType<FakeInvoiceExtractor>(extractor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("OpenAI")]
    public void Any_other_extractor_refuses_to_start(string? name)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Resolve(new() { ["InvoiceExtraction:Extractor"] = name }));

        Assert.Contains("Supported: DocumentIntelligence, Fake", ex.Message);
    }

    private static IInvoiceExtractor Resolve(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        builder.AddInvoiceExtractor();

        using var services = builder.Services.BuildServiceProvider();
        return services.GetRequiredService<IInvoiceExtractor>();
    }
}
