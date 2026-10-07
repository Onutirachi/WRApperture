---
description: "Task list for RDP alerts and language bar fixes"
---

# Tasks: Abrir apps sem alerta e sem barra de idioma

## Phase 1: US2 - Barra de idioma (host)

- [X] T001 [P] `IUserRegistryAccess` + `WindowsUserRegistryAccess` + fake
- [X] T002 Testes do check `LanguageBarHidden` (pendente, prepare, idempotência, revert por SID, sem hives carregados)
- [X] T003 `LanguageBar` + check em `HostPreparation` + DI
- [X] T004 `wrahost langbar hide|show|status` (usuário atual, sem admin)

## Phase 2: US1 - Assinatura (cliente)

- [X] T005 [P] Testes: `ThumbprintList` (adicionar/remover preservando outros), SDDL com vários SIDs, comando `rdpsign`, launcher assina só quando configurado e abre mesmo com falha
- [X] T006 `ThumbprintList`, `IProcessRunner`, `RdpSigning` (status, sign, setup, remove)
- [X] T007 Launcher integra a assinatura
- [X] T008 `App`: `--setup-signing <SID>` e `--remove-signing`. ViewModel: convite no primeiro clique (recusa lembrada) e item de menu
- [X] T009 Desinstalador do cliente: `--remove-signing` elevado com `-RemoveUserData`

## Phase 3: Polish

- [X] T010 Build, testes (.NET e Pester), release
- [X] T011 Validação real da configuração elevada (UAC) neste PC: certificado criado, `.rdp` assinado, nenhum alerta. Depois reverter, se o usuário quiser — OK: validado com UAC; o usuário escolheu manter configurado. Evidências em research.md R2b
- [X] T012 README e LEIA-ME (logoff da sessão para a barra de idioma)
