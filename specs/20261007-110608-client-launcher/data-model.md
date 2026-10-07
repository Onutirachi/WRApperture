# Data Model: Client Launcher

## PairedHost (`hosts.json`)
| Campo | Tipo | Regra |
|---|---|---|
| `Id` | Guid | Gerado no pareamento. Usado em nomes de pasta |
| `DisplayName` | string | Padrão = `hostName` do agente |
| `Address` | string | Como digitado (nome ou IP). Fonte única do `full address` |
| `AgentPort` | int | 1024–65535, padrão 5080 |
| `ProtectedToken` | string | Base64 de DPAPI (CurrentUser) |
| `PinnedFingerprint` | string | SHA-256 hex maiúsculo |
| `RdpPort` | int | Do `/api/host`, padrão 3389 |
| `RdpUserName` | string? | Preenchido quando o usuário salva credenciais |
| `Apps` | `CachedApp[]` | Última lista conhecida |
| `LastSyncUtc` | DateTimeOffset? | |

## CachedApp
`Alias`, `DisplayName`, `IconETag?`.

## ClientState (raiz do `hosts.json`)
`Version` (=1), `SelectedHostId?`, `Hosts: PairedHost[]`.

## Exceções (Client.Core)
| Exceção | Quando |
|---|---|
| `InvalidTokenException` | 401 |
| `TooManyAttemptsException` | 429 |
| `AgentUnreachableException` | DNS, conexão recusada ou timeout (10 s) |
| `CertificateChangedException(Observed)` | O fingerprint não bate com o pin |
| `PairingMismatchException` | No pareamento, o fingerprint observado ≠ o informado pelo agente |
