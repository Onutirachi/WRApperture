# Data Model: Host Core

Todos os tipos ficam em `WRApperture.Host.Core` e são `record`s imutáveis.

## PublishedApp
| Campo | Tipo | Regra |
|---|---|---|
| `Alias` | string | `^[A-Za-z0-9_-]{1,64}$`, único (case-insensitive). É a chave em `Applications\<Alias>` |
| `DisplayName` | string | 1–128 chars, obrigatório |
| `ExecutablePath` | string | Absoluto após expandir env vars. O arquivo existe e é `.exe` |
| `RawPath` | string? | Caminho original (com `%VAR%`) → `VPath` |
| `IconPath` | string? | Default = `ExecutablePath` |
| `IconIndex` | int | ≥ 0, default 0 |
| `CommandLinePolicy` | enum | `None`(0) / `Any`(1) / `Required`(2) |
| `RequiredArguments` | string? | Obrigatório quando a policy é `Required` |
| `ShowInList` | bool | Default `true` → `ShowInTSWA` |
| `IsManaged` | bool | `true` se criado pela ferramenta (marcador `WRApperture=1` na subchave). Só informativo. Apps não gerenciados também podem ser editados e removidos individualmente |

## PublishRequest
Entrada de `Publish`. Tem os mesmos campos de `PublishedApp`, exceto `IsManaged`. O `Alias` é opcional e, se ausente, é gerado a partir de `DisplayName` (research R5).

## PublishResult
| Campo | Tipo |
|---|---|
| `App` | PublishedApp |
| `Outcome` | enum `Created` / `Updated` / `Unchanged` |
| `Warnings` | IReadOnlyList<string> (ex.: `WritableLocation`) |

## DiscoveredApp
| Campo | Tipo | Regra |
|---|---|---|
| `DisplayName` | string | Nome do `.lnk` sem extensão |
| `ExecutablePath` | string | Alvo resolvido, `.exe` existente |
| `Arguments` | string? | Do atalho |
| `IconPath` / `IconIndex` | string / int | Do atalho, com fallback no exe |
| `Source` | enum `AllUsers` / `CurrentUser` |
| `PublishedAlias` | string? | Preenchido quando o mesmo exe+args já está publicado |

## ReadinessCheck
| Campo | Tipo |
|---|---|
| `Id` | enum `Edition`, `RdpEnabled`, `Nla`, `AllowListEnforced`, `FirewallRdp` |
| `Description` | string (PT-BR) |
| `State` | enum `Ok` / `Pending` / `NotSupported` |
| `CurrentValue` / `ExpectedValue` | string |

**Transições**: `Pending → Ok` via `Prepare`. `Ok → (estado anterior)` via `Revert`, só para os itens presentes no snapshot. `NotSupported` é terminal: nem `Prepare` nem `Revert` agem.

## PreparationSnapshot
Fica em `HKLM\SOFTWARE\WRApperture\PreparationSnapshot`, com um valor por `ReadinessCheck.Id` alterado. O formato é `"<existia:0|1>;<tipo>;<valor anterior>"`, ou `"<0|1>;<lista de nomes de regra>"` para o firewall. Uma vez gravado, o valor não é sobrescrito, para preservar o estado original. O snapshot é apagado depois de um `Revert` bem-sucedido.
