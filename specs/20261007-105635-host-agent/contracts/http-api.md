# Contract: Host Agent HTTP API (v1)

Base: `https://<host>:<port>` (padrão 5080). JSON em camelCase. As constantes ficam em `WRApperture.Shared.ApiContract`.

| Método | Rota | Auth | Sucesso | Erros |
|---|---|---|---|---|
| GET | `/health` | não | `200 text/plain "ok"` | — |
| GET | `/api/host` | token | `200 HostInfoDto` | 401, 429 |
| GET | `/api/apps` | token | `200 AppSummaryDto[]` (só `ShowInList=true`, ordenado por nome) | 401, 429 |
| GET | `/api/apps/{alias}/icon?size=48` | token | `200 image/png` + `ETag`. `304` se `If-None-Match` casar | 400 (size fora de 16–256), 401, 404, 429 |

**Auth**: header `X-WRApperture-Token: <token>`.
- Token ausente, inválido ou host não pareado: `401` com `ApiErrorDto { error: "unauthorized" }`.
- 10 falhas em 5 min do mesmo IP: `429` com `{ error: "too_many_attempts" }` por 5 min. A resposta 429 vem antes de validar o token.

**Exemplos**
```json
// GET /api/host
{ "hostName": "PC-ESCRITORIO", "rdpPort": 3389, "agentVersion": "0.1.0", "certificateFingerprint": "AB12..." }
// GET /api/apps
[ { "alias": "bloco-de-notas", "displayName": "Bloco de Notas", "iconUrl": "/api/apps/bloco-de-notas/icon" } ]
```

## CLI (extensão do `wrahost`)
```
wrahost agent show                 # porta, pareado (sim/não), fingerprint
wrahost agent token                # (admin) gera/regenera e imprime o token uma vez
wrahost agent port <n>             # (admin) define a porta (reinicie o agente)
```
