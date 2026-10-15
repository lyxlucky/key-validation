namespace KeyValidation.Application;
public record LoginRequest(string UserName, string Password);
public record UserDto(string Id, string UserName, IReadOnlyList<string> Roles, bool MustChangePassword = false, bool TwoFactorEnabled = false);
public record CsrfDto(string Token);
