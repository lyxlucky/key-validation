using KeyValidation.Domain;

namespace KeyValidation.Application;
public sealed record ClientVersionOptions(bool AllowLoopbackHttp = false);
