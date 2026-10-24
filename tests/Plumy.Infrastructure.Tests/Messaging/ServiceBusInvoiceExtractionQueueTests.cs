using Azure.Messaging.ServiceBus;
using Plumy.Application.Invoices.Extraction;
using Plumy.Infrastructure.Messaging;

namespace Plumy.Infrastructure.Tests.Messaging;

public class ServiceBusInvoiceExtractionQueueTests(ServiceBusFixture fixture) : IClassFixture<ServiceBusFixture>
{
    [Fact]
    public async Task Enqueue_SendsAMessageTheWorkerCanRead()
    {
        var request = new InvoiceExtractionRequest(Guid.CreateVersion7(), Guid.CreateVersion7());
        await using var queue = new ServiceBusInvoiceExtractionQueue(fixture.Client);

        await queue.EnqueueAsync(request);

        await using var receiver = fixture.Client.CreateReceiver(InvoiceExtractionMessages.QueueName);
        var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(30));
        Assert.NotNull(message);
        await receiver.CompleteMessageAsync(message);

        Assert.Equal(request, InvoiceExtractionMessages.FromMessage(message));
        Assert.Equal(InvoiceExtractionMessages.Subject, message.Subject);
        Assert.Equal("application/json", message.ContentType);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"invoiceId":"00000000-0000-0000-0000-000000000000","firmId":"3f1c2d4e-0000-0000-0000-000000000001"}""")]
    [InlineData("""{"invoiceId":"3f1c2d4e-0000-0000-0000-000000000001"}""")]
    [InlineData("null")]
    public void FromMessage_WithABrokenBody_IsNull(string body)
    {
        var message = ServiceBusModelFactory.ServiceBusReceivedMessage(body: BinaryData.FromString(body));

        Assert.Null(InvoiceExtractionMessages.FromMessage(message));
    }
}
