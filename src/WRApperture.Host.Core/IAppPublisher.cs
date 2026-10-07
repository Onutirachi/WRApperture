using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core;

public interface IAppPublisher
{
    /// <summary>All published RemoteApps, including ones created outside this tool. Never requires admin.</summary>
    IReadOnlyList<PublishedApp> List();

    PublishedApp? Get(string alias);

    /// <summary>Creates or updates an app. Idempotent. Requires admin.</summary>
    PublishResult Publish(PublishRequest request);

    /// <summary>Removes only the given app. Returns false when it did not exist. Requires admin.</summary>
    bool Unpublish(string alias);
}
