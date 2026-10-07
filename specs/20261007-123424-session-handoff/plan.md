# Implementation Plan: Alternância entre uso local do host e apps remotos

**Branch**: `20261007-123424-session-handoff` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

**Host.Core / Sessions**:
- `ISessionProvider` (WTS: listar, encerrar e iniciar o shell numa sessão), com uma implementação Windows via P/Invoke e um fake para testes.
- `SessionPolicy` (pura, testada): decide as sessões que bloqueiam e o usuário RDP correspondente.
- `SessionKind` é inferido pelos processos da sessão: `explorer.exe` indica Desktop, e `rdpshell.exe`/`rdpinit.exe` sem Explorer indicam RemoteApp.

**Agent**:
- `GET /api/session?user=` retorna as sessões que bloqueiam.
- `POST /api/session/release` encerra essas sessões e espera a conclusão.
- O `SessionWatcher` (BackgroundService, a cada 3 s) inicia `explorer.exe` (via `CreateProcessAsUser` com `WTSQueryUserToken`) quando a sessão da tela do host é uma RemoteApp.

**Client**:
- Verificação antes de abrir o app, diálogo de confirmação com a opção "sempre encerrar neste host" e item de menu para desligar o automático.
- O `AgentClient` ganha timeout por requisição (45 s no release).

## Research

- **WTS**: `WTSEnumerateSessionsW`, `WTSQuerySessionInformationW` (`WTSUserName`=5, `WTSDomainName`=7), `WTSGetActiveConsoleSessionId`, `WTSLogoffSession(bWait=true)`, `WTSQueryUserToken` (exige SE_TCB, ou seja, LocalSystem). Todas são nativas e não precisam de pacote.
- **Conta Microsoft**: o nome local costuma ser o início do e-mail (até 5 caracteres no Windows 10/11). A correspondência compara se a parte local do e-mail começa com o nome da sessão (com no mínimo 5 caracteres).
- **Por que logoff e não desconectar**: uma sessão de área de trabalho desconectada do mesmo usuário seria reconectada pelo RDP e o RemoteApp falharia do mesmo jeito.
- **Não verificado (exige dois PCs)**: o comportamento exato ao anexar a tela do host a uma sessão RemoteApp e o resultado do `explorer.exe` iniciado nela. Validação pelo usuário (quickstart).

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| II. Registro via Core | ✅ N/A | Sem acesso a registro |
| III. Idempotente | ✅ | O release sem bloqueios não faz nada, e o watcher trata cada sessão uma única vez |
| IV. Segurança | ✅ | Endpoints exigem token. Nunca encerra sessões de outros usuários. Confirmação explícita no cliente por padrão |
| V. Observabilidade | ✅ | Log de cada logoff e de cada início de shell |

## Contract (API v1, novos endpoints)

```
GET  /api/session?user=<rdpUser opcional>  → 200 SessionStatusDto { busy, sessions: [{ user, state, isConsole }] }
POST /api/session/release  { user }         → 200 ReleaseResultDto { released, busy }   (aguarda até 45 s)
```
