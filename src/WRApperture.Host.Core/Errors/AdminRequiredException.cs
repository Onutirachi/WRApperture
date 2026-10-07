using WRApperture.Shared.Localization;

namespace WRApperture.Host.Core.Errors;

public sealed class AdminRequiredException()
    : InvalidOperationException(Strings.T("host.errors.adminRequired"));
