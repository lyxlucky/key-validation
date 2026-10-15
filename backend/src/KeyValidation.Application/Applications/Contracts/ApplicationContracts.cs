namespace KeyValidation.Application;
public record ApplicationDto(Guid Id, string Code, string Name, DateTimeOffset? DisabledAt, DateTimeOffset CreatedAt);
public record CreateApplication(string Code, string Name);
