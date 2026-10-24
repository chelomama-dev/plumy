using Azure.Messaging.ServiceBus;

namespace Plumy.Worker.Tests;

/// <summary>Records how each message was settled instead of talking to Service Bus.</summary>
internal sealed class FakeReceiver : ServiceBusReceiver
{
    public List<string> Settlements { get; } = [];

    public string? DeadLetterReason { get; private set; }

    public override Task CompleteMessageAsync(
        ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
    {
        Settlements.Add("Complete");
        return Task.CompletedTask;
    }

    public override Task AbandonMessageAsync(ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = default)
    {
        Settlements.Add("Abandon");
        return Task.CompletedTask;
    }

    public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, string deadLetterReason,
        string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
    {
        Settlements.Add("DeadLetter");
        DeadLetterReason = deadLetterReason;
        return Task.CompletedTask;
    }

    public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message,
        IDictionary<string, object>? propertiesToModify, string deadLetterReason,
        string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default) =>
        DeadLetterMessageAsync(message, deadLetterReason, deadLetterErrorDescription, cancellationToken);
}
