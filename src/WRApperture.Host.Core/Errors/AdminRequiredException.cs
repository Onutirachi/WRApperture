namespace WRApperture.Host.Core.Errors;

public sealed class AdminRequiredException()
    : InvalidOperationException("Esta operação requer administrador. Execute o terminal como administrador.");
