namespace WRApperture.Host.Core.Errors;

public sealed class HostNotSupportedException(string edition)
    : InvalidOperationException($"Edição do Windows não suportada para RemoteApp: {edition}. Use Pro, Enterprise ou Education.")
{
    public string Edition { get; } = edition;
}
