using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record CreateChannel(string Name, string AccountId, Guid[] ApplicationIds, long InitialQuota = 0);
public record UpdateChannel(string Name, string AccountId, Guid[] ApplicationIds, [property: JsonRequired] bool IsEnabled);
public record AdjustChannelQuota(Guid RequestId, long Delta, string Reason);
public record SetChannelPrice(Guid ApplicationId, Guid PlanId, long UnitPriceCents);
public record ChannelPriceDto(Guid ApplicationId, Guid PlanId, long UnitPriceCents);
public record ChannelDto(Guid Id, string Name, string AccountId, Guid[] ApplicationIds, long RemainingQuota, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ChannelPriceDto> Prices);
public record ChannelQuotaDto(Guid Id, Guid ChannelId, string Kind, long Delta, long BalanceAfter, string Reason, string ActorId, DateTimeOffset CreatedAt, Guid? OrderId, Guid RequestId);
