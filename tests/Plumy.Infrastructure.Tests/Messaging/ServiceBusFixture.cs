using Azure.Messaging.ServiceBus;
using Testcontainers.ServiceBus;

namespace Plumy.Infrastructure.Tests.Messaging;

/// <summary>
/// The Service Bus emulator (plus the SQL Server it keeps its state in), with the same queue as Azure. Slow to
/// start, so one per test class.
/// </summary>
public sealed class ServiceBusFixture : IAsyncLifetime
{
    // Same emulator version as the Aspire AppHost runs locally
    private readonly ServiceBusContainer _container =
        new ServiceBusBuilder("mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.0")
            .WithAcceptLicenseAgreement(true)
            .WithConfig(Path.Combine(AppContext.BaseDirectory, "Messaging", "servicebus-emulator.json"))
            .Build();

    public ServiceBusClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Client = new ServiceBusClient(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
        await _container.DisposeAsync();
    }
}
