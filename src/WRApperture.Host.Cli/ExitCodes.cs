using WRApperture.Host.Core.Errors;
using WRApperture.Shared.Localization;
using Serilog;

namespace WRApperture.Host.Cli;

/// <summary>Maps Core exceptions to the exit codes in contracts/core-api.md.</summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int ValidationError = 1;
    public const int AdminRequired = 2;
    public const int NotSupported = 3;
    public const int Unexpected = 10;

    public static int Run(Action action)
    {
        try
        {
            action();
            return Success;
        }
        catch (AppValidationException ex)
        {
            Output.Error(Strings.T("cli.errors.validation", ex.Message));
            return ValidationError;
        }
        catch (AdminRequiredException ex)
        {
            Output.Error(ex.Message);
            return AdminRequired;
        }
        catch (HostNotSupportedException ex)
        {
            Output.Error(ex.Message);
            return NotSupported;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected failure");
            Output.Error(Strings.T("cli.errors.unexpected", ex.Message));
            return Unexpected;
        }
    }
}
