using System.Security.Claims;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;
public record RoleDto(string Id, string Name, string? Description, bool IsBuiltIn, string[] Permissions, int UserCount);
public record SaveRoleRequest(string Name, string[] Permissions, string? Description = null);
