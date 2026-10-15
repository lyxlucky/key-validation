using KeyValidation.Domain;

namespace KeyValidation.Application;
public record PermissionDto(string Code, string Label, string Group, string Description);
