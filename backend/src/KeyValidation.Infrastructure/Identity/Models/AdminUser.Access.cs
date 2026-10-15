using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AdminUser
{
    public bool AccessConfigured { get; set; }
    public bool AccessAllApplications { get; set; } = true;
    public Guid[] AccessApplicationIds { get; set; } = [];
    public string[] AccessPermissions { get; set; } = [];
    public Guid? AccessChannelId { get; set; }
}
