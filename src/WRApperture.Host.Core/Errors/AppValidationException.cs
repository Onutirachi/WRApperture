namespace WRApperture.Host.Core.Errors;

public sealed class AppValidationException(string field, string reason)
    : ArgumentException($"{field}: {reason}")
{
    public string Field { get; } = field;
    public string Reason { get; } = reason;
}
