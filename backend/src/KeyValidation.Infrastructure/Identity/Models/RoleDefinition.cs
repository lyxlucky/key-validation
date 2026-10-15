using KeyValidation.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class RoleDefinition
{
    public string RoleId { get; set; } = "";
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
    // A legacy API configuration owns one explicit role, never an extra grant engine.
    public string? LegacyUserId { get; set; }
}
