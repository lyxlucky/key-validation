namespace KeyValidation.Application;
public interface IDatabaseAvailabilityProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
