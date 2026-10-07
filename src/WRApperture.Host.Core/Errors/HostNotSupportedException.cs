using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Errors;

public sealed class HostNotSupportedException(string edition)
    : InvalidOperationException(Strings.T("host.errors.editionNotSupported", edition))
{
    public string Edition { get; } = edition;
}
