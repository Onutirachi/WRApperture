# Implementation Plan: Packaging

**Branch**: `20261007-112159-packaging` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/20261007-112159-packaging/spec.md`

## Summary

`scripts/build-release.ps1` roda os testes e publica os três executáveis como self-contained single-file (win-x64). Em seguida monta dois zips:
- **Host**: o instalador elevado copia os binários para `Program Files`, roda `wrahost prepare`, gera o token se necessário, registra o serviço `WRAppertureAgent` com recuperação, cria a regra de firewall da porta configurada, inicia o serviço e verifica `/health`.
- **Cliente**: o instalador por usuário copia o executável para `%LOCALAPPDATA%\Programs` e cria o atalho no Menu Iniciar.

Todo acesso ao registro passa pelo `wrahost`, que ganha `agent show --json`, `agent token --json` e `agent reset`. Os detalhes estão em [research.md](research.md).

## Technical Context

**Language/Version**: Windows PowerShell 5.1 (scripts) e C# 12 / .NET 8 (extensões do CLI e do Core)

**Primary Dependencies**: Nenhuma nova. Usa `dotnet publish`, os módulos `NetSecurity`/`NetTCPIP` (nativos do Windows), `sc.exe` e `curl.exe`. Testes com Pester 3.4 (nativo).

**Storage**: N/A (usa Agent Settings via `wrahost`).

**Testing**: xUnit (Core: `Reset`) e Pester (scripts: dry-run, recusa sem admin, instalação real do cliente em diretórios temporários).

**Target Platform**: Windows 10/11 x64.

**Project Type**: Build e instalação.

**Constraints**: Os scripts não gravam no registro (constituição II). Idempotentes (III). Caminho do serviço entre aspas e firewall sem perfil Public por padrão (IV).

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | Nenhuma dependência nova. PowerShell 5.1 nativo justificado em R2 |
| II. Registro via Core | ✅ | Os scripts chamam `wrahost prepare/revert/agent *`. `Reset()` fica no Core, com teste |
| III. Idempotente/reversível | ✅ | Reinstalar = atualizar. Desinstalar é idempotente. `-Purge` reverte a preparação |
| IV. Segurança | ✅ | `Program Files` (não gravável por usuários comuns), binPath entre aspas, firewall Domain/Private, token só exibido na geração |
| V. Observabilidade | ✅ | O instalador mostra um trecho do log do agente quando a verificação falha |

## Project Structure

```text
scripts/build-release.ps1
packaging/
├── host/   Instalar.cmd, Install-WRAppertureHost.ps1, Uninstall-WRAppertureHost.ps1, LEIA-ME.txt
└── client/ Instalar.cmd, Install-WRAppertureClient.ps1, Uninstall-WRAppertureClient.ps1
src/WRApperture.Host.Core/Agent/AgentSettingsStore.cs   (+ Reset)
src/WRApperture.Host.Cli/AgentCommands.cs               (+ --json, reset)
tests/packaging/*.Tests.ps1
```

## Complexity Tracking

Sem violações.
