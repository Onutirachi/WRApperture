namespace WRApperture.Client.Core.Agent;

/// <summary>Base for agent errors. Messages are user-facing (PT-BR) and suggest an action.</summary>
public class AgentException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class InvalidTokenException()
    : AgentException("Token de pareamento inválido ou revogado. Gere um novo com 'wrahost agent token' no host e informe-o aqui.");

public sealed class TooManyAttemptsException()
    : AgentException("O host bloqueou temporariamente este computador por excesso de tentativas. Aguarde alguns minutos.");

public sealed class AgentUnreachableException(string address, int port, Exception? inner = null)
    : AgentException($"Não foi possível conectar ao host {address}:{port}. Verifique o endereço, se o agente está rodando e se a porta está liberada no firewall.", inner);

public sealed class CertificateChangedException(string observedFingerprint)
    : AgentException("A identidade do host mudou desde o pareamento. Se o certificado do host foi renovado, confirme o novo fingerprint; caso contrário, não continue.")
{
    public string ObservedFingerprint { get; } = observedFingerprint;
}

public sealed class PairingMismatchException()
    : AgentException("A identidade informada pelo host não confere com a da conexão. Pode haver interceptação na rede; o pareamento foi cancelado.");
