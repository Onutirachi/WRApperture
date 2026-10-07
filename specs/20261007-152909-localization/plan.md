# Implementation Plan: Interface em vários idiomas

**Branch**: `20261007-152909-localization` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

- **Catálogo** (`WRApperture.Shared/Localization/Strings.cs`):
  - os arquivos `localization/<idioma>/<área>.json` são embutidos como `loc/<idioma>/<área>.json`, e os idiomas são descobertos pelos nomes das pastas;
  - a ordem de busca sai da cultura de UI do Windows (`CultureInfo.Parent` até a cultura neutra) e termina em `en`;
  - `ChainFor`/`Lookup` são funções puras, usadas nos testes;
  - `Use` força um idioma.
- **Escolha do idioma**: `WRAPPERTURE_LANG`, depois `HKCU\Software\WRApperture\Ui\Language`, depois `CultureInfo.CurrentUICulture`.
- **XAML**: `TExtension` em `WRApperture.Ui` (`{loc:T área.chave}`). O idioma é resolvido na abertura, sem troca ao vivo.
- **Scripts**: os instaladores leem os mesmos JSONs (`installer.json`), copiados para os pacotes e para a pasta de instalação, com a mesma ordem de busca.
- **Testes**:
  - `LocalizationTests` confere chaves e placeholders de todo idioma contra o `en` e toda chave usada em `.cs`, `.xaml` e `.ps1`;
  - os projetos de teste fixam o inglês (`TestLanguage`), então as mensagens são verificadas em inglês em qualquer máquina.

## Execução

A extração foi dividida por área, cada uma numa cópia isolada do repositório (worktree), e depois integrada:

| Área | Arquivos de tradução |
|---|---|
| Cliente (`Client`, `Client.Core`) | `client.json` |
| App do host (`Host.Manager`) | `manager.json` |
| Núcleo do host, agente e `wrahost` | `host.json`, `agent.json`, `cli.json` |
| Instaladores e `build-release.ps1` | `installer.json` |

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | Sem pacotes novos (System.Text.Json do runtime) |
| II. Registro via Core | ✅ | Só leitura de uma preferência opcional no HKCU |
| III. Idempotente | ✅ | — |
| IV. Segurança | ✅ | Textos não executam nada; placeholders conferidos por teste |
| V. Observabilidade | ✅ | Logs continuam em inglês, independentes do idioma da interface |
