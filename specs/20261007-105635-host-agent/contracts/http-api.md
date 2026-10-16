# Contract: Host Agent HTTP API (v1)

Base: `https://<host>:<port>` (padrão 5080). JSON em camelCase. As constantes ficam em `WRApperture.Shared.ApiContract`.

| Método | Rota | Auth | Sucesso | Erros |
|---|---|---|---|---|
| GET | `/health` | não | `200 text/plain "ok"` | — |
| GET | `/api/host` | token | `200 HostInfoDto` | 401, 429 |
| GET | `/api/apps` | token | `200 AppSummaryDto[]` (só `ShowInList=true`, ordenado por nome) | 401, 429 |
| GET | `/api/apps/{alias}/icon?size=48` | token | `200 image/png` + `ETag`. `304` se `If-None-Match` casar | 400 (size fora de 16–256), 401, 404, 429 |
| GET | `/api/session?user=<rdp>&mode=remoteapp\|desktop` | token | `200 SessionStatusDto` (sessões no caminho e a ação de cada uma) | 400 (modo desconhecido), 401, 429 |
| POST | `/api/session/release` `{ user, mode }` | token | `200 SessionReleaseResultDto` | 400, 401, 429 |

**Sessões** (`20261007-123424-session-handoff`, modo `desktop` em `20261007-150633-full-desktop`):
- Sem `mode`, ou com `remoteapp`, a regra é: sessões de área de trabalho do usuário recebem logoff e sessões ativas de outros usuários são desconectadas.
- Com `desktop`, as sessões RemoteApp do usuário recebem logoff, a área de trabalho dele é reaproveitada e as sessões ativas de outros usuários são desconectadas.
- O modo `desktop` só existe a partir do agente 0.6.0. Esses agentes informam `features: ["desktop-session"]` em `/api/host`; o cliente só usa o modo quando a feature aparece.

**Auth**: header `X-WRApperture-Token: <token>`.
- Token ausente, inválido ou host não pareado: `401` com `ApiErrorDto { error: "unauthorized" }`.
- 10 falhas em 5 min do mesmo IP: `429` com `{ error: "too_many_attempts" }` por 5 min. A resposta 429 vem antes de validar o token.

**Exemplos**
```json
// GET /api/host
{ "hostName": "PC-ESCRITORIO", "rdpPort": 3389, "agentVersion": "0.6.0", "certificateFingerprint": "AB12...", "features": ["desktop-session"] }
// GET /api/apps
[ { "alias": "bloco-de-notas", "displayName": "Bloco de Notas", "iconUrl": "/api/apps/bloco-de-notas/icon" } ]
```

## Prontidão e pareamento por aprovação (`20261015-114136-easy-setup`)

Esses recursos são anunciados em `features` como `readiness` e `pairing`.

- `GET /api/host` passa a trazer também:
  - `issues`: códigos de `HostIssues`, como `public-network` e `account-without-password`. O cliente traduz os códigos e ignora os desconhecidos.
  - `suggestedUser`: `PC\usuario` ou o e-mail da conta Microsoft.
  - `microsoftAccount`: se o usuário sugerido é uma conta Microsoft.
- `POST /pair/requests`, sem token: `{ clientName, clientUser, nonce }`.
  - Sucesso: `202 { id, expiresInSeconds }`.
  - `409 no_approver`: ninguém logado no host.
  - `429 pairing_limited`: um pedido pendente por IP, 5 por IP a cada 10 min, 3 pendentes no total.
  - `400 bad_request`: pedido inválido.
- `GET /pair/requests/{id}`: `{ state, token? }`, com `state` em `pending`, `approved`, `denied` ou `expired`. O token vem só na primeira resposta `approved`. `404` quando o pedido não existe.
- `GET|POST /pair/approval/{id}`: só por loopback, com o header `X-WRApperture-Approval: <segredo>`. Usado pela janela que o agente abre na sessão do console (`WRApperture.Host.exe --pairing <id> <segredo>`).
- Código de verificação: 6 dígitos de `SHA-256("WRApperture-pairing|<fingerprint>|<nonce>")` (veja `PairingCode`).
- Descoberta: datagrama UDP de 512 bytes para a porta 5081 (`WRAPPERTURE-DISCOVER/1` seguido de espaços). A resposta, em JSON, é `{ hostName, agentPort, fingerprint, version, features }` e nunca passa de 512 bytes.

## Mudanças em tempo real (`20261016-142601-live-sync`)

Anunciado em `features` como `changes`, a partir do agente 0.20.0.

- `GET /api/changes?since=<versão>`, com token: long polling, `200 ChangesDto { version }`.
  - Sem `since`, ou com uma versão diferente da atual, a resposta é imediata.
  - Com a versão atual, o agente segura a resposta até algo mudar, por no máximo 50 s (`ApiContract.ChangesWaitSeconds`). Depois desse tempo devolve a mesma versão.
- A versão é opaca. Ela muda junto com o que `/api/apps` e `/api/host` mostram: apps visíveis, nome do host e porta RDP, problemas de acesso, VPNs e versão do agente. O agente a recalcula no máximo a cada 2 s.
- Quando a versão muda, o cliente sincroniza o host. Quando o host não responde, o cliente marca o host como offline e tenta de novo a cada 30 s.

## Controle remoto (`20261016-175109-remote-control`)

Anunciado em `features` como `remote-control`, a partir do agente 0.21.0. Só age em sessões RemoteApp, nunca nas de área de trabalho.

- `GET /api/host` traz `managerAlias`: o alias do app do host (WRApperture.Host.exe) para o botão "Gerenciar host", ou `null`.
  - O agente publica `wrapperture-host` oculto (`ShowInList=false`) quando o app ainda não está publicado.
  - Se ele já foi publicado à mão, o alias é o dessa entrada.
- `GET /api/apps`: cada app traz `running`, que indica se ele está aberto numa sessão RemoteApp. Um app conta como aberto quando o caminho do processo é igual ao executável publicado. O estado entra na versão de `/api/changes`.
- `POST /api/apps/{alias}/close`: roda `taskkill /PID …`, sem `/F`, na sessão do app, como clicar no X.
- `POST /api/apps/{alias}/kill`: encerra os processos do app.
- As duas rotas respondem `200 AppStopResultDto { processes }`, ou `404` quando o app não existe.
- `POST /api/session/end` `{ user }`: faz logoff das sessões RemoteApp do usuário, ou de todas quando `user` é `null`. Responde `200 SessionEndResultDto { ended }`.

## CLI (extensão do `wrahost`)
```
wrahost agent show                 # porta, pareado (sim/não), fingerprint
wrahost agent token                # (admin) gera/regenera e imprime o token uma vez
wrahost agent port <n>             # (admin) define a porta (reinicie o agente)
```
