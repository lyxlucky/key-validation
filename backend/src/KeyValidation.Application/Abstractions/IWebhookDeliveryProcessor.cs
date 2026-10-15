namespace KeyValidation.Application;
public interface IWebhookDeliveryProcessor
{
    Task<int> DeliverDueAsync(CancellationToken cancellationToken = default);
}
