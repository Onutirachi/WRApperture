# Implementation Plan: Abrir apps sem alerta e sem barra de idioma

**Branch**: `20261007-120151-rdp-alerts-langbar` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

**Cliente**: `RdpSigning` em Client.Core.
- **Configurar** (elevado): chave CNG de máquina com ACL por usuário, certificado em `LocalMachine\My` e thumbprint na política de publicadores confiáveis do RDP.
- **Assinar** (não elevado): `rdpsign` aplicado a cada `.rdp` gerado.
- O launcher assina quando a configuração está feita. A UI oferece a configuração no primeiro clique em um app.

**Host**: `HostPreparation` ganha o check `LanguageBarHidden` (hives carregados via `IUserRegistryAccess`), e o CLI ganha `wrahost langbar hide|show`.

Os detalhes estão em [research.md](research.md).

## Technical Context

- **Language/Version**: .NET 8, sem pacotes novos (`System.Security.Cryptography.Cng` faz parte do runtime).
- **Testing**:
  - Unitários: lista de thumbprints, SDDL e montagem do comando `rdpsign` (fake runner), launcher assina ou não, check e snapshot da barra de idioma (fake de HKU).
  - Validação manual da parte elevada no PC do usuário (UAC).

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| II. Registro via Core | ✅ | Host: HKU via `IUserRegistryAccess` no Core. Cliente: a política do RDP é escrita só por `RdpSigning` (Client.Core) |
| III. Idempotente/reversível | ✅ | Setup idempotente e multiusuário. `--remove-signing` reverte. Barra de idioma com snapshot e revert |
| IV. Segurança | ✅ | Chave não exportável e liberada só para os SIDs configurados. Cópia no Root **com propósito restrito** a Remote Desktop Authentication: o Authenticode recusa, o que foi verificado. Sem TrustedPublisher. A proteção contra `.rdp` de terceiros continua (verificado) |

## Project Structure

```text
src/WRApperture.Host.Core/Abstractions/IUserRegistryAccess.cs
src/WRApperture.Host.Core/Platform/WindowsUserRegistryAccess.cs
src/WRApperture.Host.Core/Readiness/LanguageBar.cs, HostPreparation.cs (+check)
src/WRApperture.Host.Cli/LanguageBarCommands.cs
src/WRApperture.Client.Core/Signing/RdpSigning.cs, ThumbprintList.cs, IProcessRunner
src/WRApperture.Client.Core/Launch/RemoteAppLauncher.cs (assina)
src/WRApperture.Client/App.xaml.cs (--setup-signing/--remove-signing), MainViewModel (convite + menu)
packaging/client/Uninstall-WRAppertureClient.ps1 (remove-signing com -RemoveUserData)
```
