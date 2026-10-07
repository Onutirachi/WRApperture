# Implementation Plan: Área de trabalho completa do host

**Branch**: `20261007-150633-full-desktop` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

- **Shared**: `SessionModes` (`remoteapp`, `desktop`), `AgentFeatures.DesktopSession`, `HostInfoDto.Features` e `SessionReleaseRequestDto.Mode`. Todos são opcionais, então clientes e agentes antigos continuam compatíveis.
- **Core**: `SessionPolicy.Plan(sessions, user, mode)`. O modo `desktop` troca o alvo do logoff (sessões RemoteApp do usuário em vez das de área de trabalho) e mantém a regra de desconectar os outros usuários.
- **Agente**: os endpoints de sessão repassam o modo; `/api/host` anuncia `desktop-session`.
- **Cliente Core**:
  - `PairedHost.AgentFeatures`, preenchido pelo `HostSync`;
  - `RdpFileBuilder.BuildDesktop`;
  - `RemoteAppLauncher.WriteDesktopRdpFile`/`LaunchDesktop`, que gravam `@desktop.rdp` (o `@` nunca aparece em aliases);
  - `AgentClient` com o modo.
- **Cliente UI**:
  - `AppTileViewModel.Desktop(host)` marca o bloco com `IsDesktop` e usa um ícone de monitor (Segoe MDL2);
  - o bloco fica sempre em primeiro lugar;
  - `OpenApp` e `CreateShortcut` desviam para a área de trabalho;
  - `EnsureHostFreeAsync` recebe o modo e é pulado no modo `desktop` com agente antigo.

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | Sem pacotes novos |
| II. Registro via Core | ✅ | Nada novo no registro; a regra fica em `SessionPolicy` (Core, com testes) |
| III. Idempotente | ✅ | Plano vazio quando não há o que liberar |
| IV. Segurança | ✅ | Mesmo token e mesma assinatura. O modo é validado (valor desconhecido → 400) |
| V. Observabilidade | ✅ | Logs do agente citam o modo |

## Compatibilidade

| Cliente | Agente | Resultado |
|---|---|---|
| novo | novo | Regra `desktop` |
| novo | antigo | Área de trabalho abre sem preparar o host (o Windows pergunta, como no RDP comum) |
| antigo | novo | Sem mudança (sem modo = `remoteapp`) |
