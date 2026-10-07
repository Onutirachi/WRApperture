namespace WRApperture.Host.Core.Models;

public sealed record PublishedApp(
    string Alias,
    string DisplayName,
    string ExecutablePath,
    string? RawPath,
    string IconPath,
    int IconIndex,
    CommandLinePolicy CommandLinePolicy,
    string? RequiredArguments,
    bool ShowInList,
    bool IsManaged);
