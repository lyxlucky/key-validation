namespace KeyValidation.Application;
public interface IValidationRequestMetrics
{
    void Record(string? route, int statusCode, double elapsedMilliseconds);
}
