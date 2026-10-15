namespace WRApperture.Host.Core.Models;

/// <param name="StartWith">See <see cref="PublishedApp.StartWith"/>; null keeps what an existing app has.</param>
/// <param name="AutoStartWith">See <see cref="PublishedApp.AutoStartWith"/>; null keeps what an existing app has (on for new apps).</param>
public sealed record PublishRequest(
    string ExecutablePath,
    string DisplayName,
    string? Alias = null,
    string? IconPath = null,
    int IconIndex = 0,
    CommandLinePolicy CommandLinePolicy = CommandLinePolicy.None,
    string? RequiredArguments = null,
    bool ShowInList = true,
    IReadOnlyList<string>? StartWith = null,
    bool? AutoStartWith = null);
