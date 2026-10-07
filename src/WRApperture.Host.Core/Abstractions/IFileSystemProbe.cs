namespace WRApperture.Host.Core.Abstractions;

public interface IFileSystemProbe
{
    bool FileExists(string path);

    /// <summary>Expands %VARIABLES% in a path.</summary>
    string Expand(string path);

    /// <summary>True when a non-administrator user could replace files in the folder holding <paramref name="filePath"/>.</summary>
    bool IsUserWritableLocation(string filePath);
}
