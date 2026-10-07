# Data Model: Host Agent

## AgentSettings (Core, `HKLM\SOFTWARE\WRApperture\Agent`)
| Campo | Registro | Regra |
|---|---|---|
| `Port` | `Port` DWORD | 1024–65535, padrão 5080 |
| `TokenHash` | `TokenHash` REG_BINARY | 32 bytes, ausente = não pareado |
| `TokenSalt` | `TokenSalt` REG_BINARY | 16 bytes |
| `CertificateThumbprint` | `CertificateThumbprint` REG_SZ | thumbprint SHA-1 em `LocalMachine\My` |

Operações (`IAgentSettingsStore`):
- `Load()`
- `RegenerateToken() → string` (admin; retorna o token em texto uma única vez)
- `VerifyToken(string) → bool` (tempo constante; `false` se não pareado)
- `SetPort(int)` (admin)
- `SetCertificateThumbprint(string)` (admin)

## HostInfo (Core, `IHostInfoProvider`)
`HostName` (string) e `RdpPort` (int, de `RDP-Tcp\PortNumber`, padrão 3389).

## DTOs (WRApperture.Shared, JSON camelCase)
- **AppSummaryDto**: `alias`, `displayName`, `iconUrl` (relativo: `/api/apps/{alias}/icon`).
- **HostInfoDto**: `hostName`, `rdpPort`, `agentVersion`, `certificateFingerprint` (SHA-256 hex).
- **ApiErrorDto**: `error` (código curto) e `message` (PT-BR).
