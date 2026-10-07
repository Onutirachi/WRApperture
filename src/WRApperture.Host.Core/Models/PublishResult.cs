namespace WRApperture.Host.Core.Models;

public enum PublishOutcome
{
    Created,
    Updated,
    Unchanged,
}

public static class PublishWarnings
{
    public const string WritableLocation = "WritableLocation";
}

public sealed record PublishResult(PublishedApp App, PublishOutcome Outcome, IReadOnlyList<string> Warnings);
