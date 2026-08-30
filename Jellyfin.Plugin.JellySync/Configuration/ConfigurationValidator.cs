namespace Jellyfin.Plugin.JellySync.Configuration;

public static class ConfigurationValidator
{
    private const string UserError = "Enabled synchronization requires at least two distinct non-empty user IDs.";
    private const string LibraryError = "Selected-library synchronization requires at least one distinct non-empty library ID.";

    public static ConfigurationValidationResult Validate(PluginConfiguration configuration)
    {
        if (!configuration.Enabled)
        {
            return new ConfigurationValidationResult(true, []);
        }

        var errors = new List<string>();

        if (DistinctNonEmptyCount(configuration.UserIds) < 2)
        {
            errors.Add(UserError);
        }

        if (!configuration.IncludeAllLibraries && DistinctNonEmptyCount(configuration.LibraryIds) < 1)
        {
            errors.Add(LibraryError);
        }

        return new ConfigurationValidationResult(errors.Count == 0, errors);
    }

    private static int DistinctNonEmptyCount(IEnumerable<Guid> ids) =>
        ids.Where(id => id != Guid.Empty)
            .Distinct()
            .Count();
}
