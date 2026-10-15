using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public interface ICommerceData : ILicensingData
{
    DbSet<Customer> Customers { get; }
    DbSet<CommerceOrder> Orders { get; }
    DbSet<OrderLicense> OrderLicenses { get; }
    DbSet<OrderHistory> OrderHistories { get; }
    DbSet<CommerceReceipt> CommerceReceipts { get; }
    DbSet<Channel> Channels { get; }
    DbSet<ChannelPrice> ChannelPrices { get; }
    DbSet<ChannelQuotaEntry> ChannelQuotaEntries { get; }
    DbSet<PortalSession> PortalSessions { get; }
    DbSet<PortalRequest> PortalRequests { get; }
}
