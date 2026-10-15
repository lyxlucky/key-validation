namespace KeyValidation.Api.DeveloperIntegration;
public sealed record DeveloperDocument(string Id, string Title, string Topic, string Markdown, string Version);
public sealed record DeveloperDocumentSummary(string Id, string Title, string Topic, string Version);
public sealed record DeveloperApiReference(string OperationId, string Method, string Path, string Authentication, string Markdown, string Version);
