namespace Jellyfin.Plugin.JellySync.Configuration;

public sealed record ConfigurationValidationResult(bool IsValid, IReadOnlyList<string> Errors);
