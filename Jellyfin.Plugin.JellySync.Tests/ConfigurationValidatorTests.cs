using Jellyfin.Plugin.JellySync.Configuration;

namespace Jellyfin.Plugin.JellySync.Tests;

public class ConfigurationValidatorTests
{
    private static readonly Guid UserA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid LibraryA = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Disabled_configuration_is_valid_without_users_or_libraries()
    {
        var result = ConfigurationValidator.Validate(new PluginConfiguration
        {
            Enabled = false,
            IncludeAllLibraries = false,
        });

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [MemberData(nameof(InvalidUserIdSets))]
    public void Enabled_configuration_rejects_fewer_than_two_distinct_non_empty_users(Guid[] userIds)
    {
        var result = ConfigurationValidator.Validate(new PluginConfiguration
        {
            Enabled = true,
            UserIds = userIds,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("two distinct non-empty user IDs", StringComparison.Ordinal));
    }

    public static IEnumerable<object[]> InvalidUserIdSets =>
    [
        [new[] { UserA }],
        [new[] { UserA, UserA }],
        [new[] { UserA, Guid.Empty }],
    ];

    [Fact]
    public void Enabled_selected_library_mode_requires_a_non_empty_library()
    {
        var result = ConfigurationValidator.Validate(new PluginConfiguration
        {
            Enabled = true,
            UserIds = [UserA, UserB],
            IncludeAllLibraries = false,
            LibraryIds = [Guid.Empty],
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("one distinct non-empty library ID", StringComparison.Ordinal));
    }

    [Fact]
    public void Enabled_selected_library_mode_is_valid_with_distinct_users_and_a_library()
    {
        var result = ConfigurationValidator.Validate(new PluginConfiguration
        {
            Enabled = true,
            UserIds = [UserA, UserB],
            IncludeAllLibraries = false,
            LibraryIds = [LibraryA],
        });

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Enabled_all_library_mode_is_valid_with_distinct_users_and_no_libraries()
    {
        var result = ConfigurationValidator.Validate(new PluginConfiguration
        {
            Enabled = true,
            UserIds = [UserA, UserB],
            IncludeAllLibraries = true,
        });

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
