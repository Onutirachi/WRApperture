using System.Reflection;
using WRApperture.Shared;

namespace WRApperture.Host.Agent.Endpoints;

/// <summary>What this agent announces about itself (in /api/host and discovery answers).</summary>
internal static class AgentInfo
{
    public static readonly string Version =
        typeof(AgentInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static readonly IReadOnlyList<string> Features =
        [AgentFeatures.DesktopSession, AgentFeatures.SelfUpdate, AgentFeatures.Readiness, AgentFeatures.Pairing, AgentFeatures.Changes];
}
