using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record LicenseQuery(int Page = 1, int PageSize = 20, Guid? ApplicationId = null, Guid? BatchId = null, string? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null, DateTimeOffset? ActivatedFrom = null, DateTimeOffset? ActivatedTo = null, DateTimeOffset? ExpiresFrom = null, DateTimeOffset? ExpiresTo = null, string? Note = null, string? Tag = null, string? DeviceId = null, Guid? ChannelId = null);
