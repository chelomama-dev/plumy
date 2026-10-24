using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plumy.Application.Invoices.LedgerAccounts;
using Plumy.Infrastructure.Classification;

namespace Plumy.Infrastructure.Tests.Classification;

public sealed class LedgerAccountClassifierRegistrationTests
{
    private static Dictionary<string, string?> Foundry(string? endpoint, string? deployment) => new()
    {
        ["LedgerAccountSuggestion:Classifier"] = "Foundry",
        ["LedgerAccountSuggestion:Foundry:Endpoint"] = endpoint,
        ["LedgerAccountSuggestion:Foundry:Deployment"] = deployment
    };

    [Fact]
    public void Foundry_is_used_with_its_endpoint_and_deployment()
    {
        var classifier = Resolve(Foundry("https://aif-test.openai.azure.com/", "gpt-5.4-mini"));

        Assert.IsType<FoundryLedgerAccountClassifier>(classifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("aif-test.openai.azure.com")]
    [InlineData("http://aif-test.openai.azure.com/")]
    public void Foundry_without_a_valid_endpoint_refuses_to_start(string? endpoint)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(Foundry(endpoint, "gpt-5.4-mini")));

        Assert.Contains("Endpoint", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Foundry_without_a_deployment_refuses_to_start(string? deployment)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Resolve(Foundry("https://aif-test.openai.azure.com/", deployment)));

        Assert.Contains("Deployment", ex.Message);
    }

    [Fact]
    public void The_fake_is_used_only_when_asked_for()
    {
        var classifier = Resolve(new() { ["LedgerAccountSuggestion:Classifier"] = "Fake" });

        Assert.IsType<FakeLedgerAccountClassifier>(classifier);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("OpenAI")]
    public void Any_other_classifier_refuses_to_start(string? name)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Resolve(new() { ["LedgerAccountSuggestion:Classifier"] = name }));

        Assert.Contains("Supported: Foundry, Fake", ex.Message);
    }

    private static ILedgerAccountClassifier Resolve(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        builder.AddLedgerAccountClassifier();

        using var services = builder.Services.BuildServiceProvider();
        return services.GetRequiredService<ILedgerAccountClassifier>();
    }
}
