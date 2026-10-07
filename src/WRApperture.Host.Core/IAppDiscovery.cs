using WRApperture.Host.Core.Models;

namespace WRApperture.Host.Core;

public interface IAppDiscovery
{
    IReadOnlyList<DiscoveredApp> Discover();
}
