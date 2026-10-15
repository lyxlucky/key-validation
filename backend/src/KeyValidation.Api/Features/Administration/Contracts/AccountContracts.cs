using System.Security.Claims;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KeyValidation.Api;
public record AccountDto(string Id, string UserName, string[] Roles, DateTimeOffset? DisabledAt, DateTimeOffset CreatedAt, bool MustChangePassword, bool TwoFactorEnabled, string[] RoleIds);
public record CreateAccountRequest(string UserName, string Password, string? Role = null, string[]? RoleIds = null);
public record ChangeAccountRoleRequest(string Role);
public record ChangeAccountRolesRequest(string[] RoleIds);
public record ResetAccountPasswordRequest(string Password);
