namespace WRApperture.Host.Core.Models;

public sealed record PublishRequest(
    string ExecutablePath,
    string DisplayName,
    string? Alias = null,
    string? IconPath = null,
    int IconIndex = 0,
    CommandLinePolicy CommandLinePolicy = CommandLinePolicy.None,
    string? RequiredArguments = null,
    bool ShowInList = true);
