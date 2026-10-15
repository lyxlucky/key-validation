namespace KeyValidation.Domain;
public class RuleException(string code, int status = 403) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = status;
    public Guid? BatchId { get; init; }
}
