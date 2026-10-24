using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Plumy.Infrastructure.Messaging;

namespace Plumy.Worker;

/// <summary>
/// Listens on the extraction queue for as long as the host runs. In Azure, KEDA starts replicas when messages
/// arrive and removes them when the queue is empty, so this usually runs only while there is work.
/// </summary>
internal sealed partial class InvoiceExtractionWorker(
    ServiceBusClient client,
    InvoiceExtractionMessageHandler handler,
    IOptions<InvoiceExtractionOptions> options,
    ILogger<InvoiceExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var processor = client.CreateProcessor(InvoiceExtractionMessages.QueueName,
            new ServiceBusProcessorOptions
            {
                // The handler settles every message itself (complete, dead-letter, or leave it for a retry)
                AutoCompleteMessages = false,
                MaxConcurrentCalls = options.Value.MaxConcurrentCalls,
                // Keeps the lock while a slow read runs; past this, the message is redelivered as if it had failed
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5)
            });

        processor.ProcessMessageAsync += handler.HandleAsync;
        processor.ProcessErrorAsync += args =>
        {
            // Connection and settlement errors; the processor recovers by itself
            LogProcessorError(args.Exception, args.ErrorSource.ToString());
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        LogStarted(InvoiceExtractionMessages.QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The host is stopping
        }

        // Lets the messages in flight finish or be abandoned (see the handler) before the host exits
        await processor.StopProcessingAsync(CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Listening on queue {QueueName}")]
    private partial void LogStarted(string queueName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Service Bus processor error ({ErrorSource})")]
    private partial void LogProcessorError(Exception exception, string errorSource);
}
