namespace Jellyfin.Plugin.Mirrarr.Configuration;

public sealed record ConfigurationValidationResult(bool IsValid, IReadOnlyList<string> Errors);
