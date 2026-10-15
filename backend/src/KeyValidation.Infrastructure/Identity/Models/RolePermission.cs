using KeyValidation.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class RolePermission
{
    public string RoleId { get; set; } = "";
    public string Permission { get; set; } = "";
}
