namespace KeyValidation.Application;
// The caller owns its existing transaction and SaveChanges. Publishing does not
// commit; duplicate keys return the existing stable event ID within that transaction.
public interface IDurableEventPublisher
{
    Task<Guid> PublishAsync(Guid applicationId, Guid resourceId, string eventType, string safePayloadJson, string deduplicationKey, CancellationToken cancellationToken = default, Guid? channelId = null);
}
