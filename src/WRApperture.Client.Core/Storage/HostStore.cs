using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Client.Core.Model;

namespace WRApperture.Client.Core.Storage;

/// <summary>Persists <see cref="ClientState"/> as JSON. Tokens are stored only DPAPI-protected.</summary>
public sealed class HostStore(ClientPaths paths, ILogger<HostStore>? logger = null)
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WRApperture.Client.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly ILogger _logger = logger ?? NullLogger<HostStore>.Instance;

    public ClientState Load()
    {
        if (!File.Exists(paths.StateFile))
        {
            return ClientState.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ClientState>(File.ReadAllText(paths.StateFile), Json) ?? ClientState.Empty;
        }
        catch (JsonException ex)
        {
            // Keep the broken file for diagnosis instead of silently losing the user's hosts.
            var backup = paths.StateFile + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(paths.StateFile, backup, overwrite: true);
            _logger.LogError(ex, "Client state is corrupt; backed up to {Backup}", backup);
            return ClientState.Empty;
        }
    }

    /// <summary>Writes atomically (temp file + move) so a crash never leaves a half-written file.</summary>
    public void Save(ClientState state)
    {
        Directory.CreateDirectory(paths.Root);
        var temp = paths.StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, paths.StateFile, overwrite: true);
    }

    public static string ProtectToken(string token) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));

    public static string UnprotectToken(string protectedToken) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser));
}
