---
description: "Task list for RDP alerts and language bar fixes"
---

# Tasks: Abrir apps sem alerta e sem barra de idioma

## Phase 1: US2 - Barra de idioma (host)

- [ ] T001 [P] `IUserRegistryAccess` + `WindowsUserRegistryAccess` + fake
- [ ] T002 Testes do check `LanguageBarHidden` (pendente, prepare, idempotência, revert por SID, sem hives carregados)
- [ ] T003 `LanguageBar` + check em `HostPreparation` + DI
- [ ] T004 `wrahost langbar hide|show|status` (usuário atual, sem admin)

## Phase 2: US1 - Assinatura (cliente)

- [ ] T005 [P] Testes: `ThumbprintList` (adicionar/remover preservando outros), SDDL com vários SIDs, comando `rdpsign`, launcher assina só quando configurado e abre mesmo com falha
- [ ] T006 `ThumbprintList`, `IProcessRunner`, `RdpSigning` (status, sign, setup, remove)
- [ ] T007 Launcher integra a assinatura
- [ ] T008 `App`: `--setup-signing <SID>` e `--remove-signing`. ViewModel: convite no primeiro clique (recusa lembrada) e item de menu
- [ ] T009 Desinstalador do cliente: `--remove-signing` elevado com `-RemoveUserData`

## Phase 3: Polish

- [ ] T010 Build, testes (.NET e Pester), release
- [ ] T011 Validação real da configuração elevada (UAC) neste PC: certificado criado, `.rdp` assinado, nenhum alerta. Depois reverter, se o usuário quiser
- [ ] T012 README e LEIA-ME (logoff da sessão para a barra de idioma)
