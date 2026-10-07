# Implementation Plan: Grade de apps do cliente — duplo clique e nomes locais

**Branch**: `20261007-145218-client-tiles` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

- **Modelo**: `CachedApp` ganha `CustomName` (opcional) e `Name` (o nome local, ou o do host). O JSON antigo continua válido: o campo novo é opcional.
- **Sincronização**: `HostSync` copia o `CustomName` do app anterior com o mesmo alias. Apps que saem do host levam o nome junto.
- **Conexão e atalho**: `RdpFileBuilder` usa `app.Name`; o atalho já usava o nome do tile.
- **Grade**: o `ItemsControl` de botões vira uma `ListBox` (seleção única, `WrapPanel`) com o mesmo visual. O duplo clique e o Enter abrem o app, e o F2 renomeia. O botão direito seleciona o item antes de abrir o menu.
- **Renomear**: `RenameAppWindow`, uma janela pequena com o nome atual, o nome do host como referência e o botão "Restaurar original". Ela é exposta por `IDialogService.AskAppName`. O `MainViewModel` grava o resultado com `ReplaceHost` e reaplica a seleção.

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | Sem pacotes novos |
| II. Registro via Core | ✅ | O host não é alterado; nada toca no registro |
| III. Idempotente | ✅ | Renomear com o mesmo nome não muda nada. Nome igual ao do host é igual a "sem nome local" |
| IV. Segurança | ✅ | O nome passa pelo mesmo `SingleLine` do `.rdp` (sem injeção de linhas) |
| V. Observabilidade | ✅ | Log ao renomear e ao restaurar |

## Project Structure

```text
src/WRApperture.Client.Core/Model/ClientState.cs     CachedApp.CustomName, Name, WithCustomName
src/WRApperture.Client.Core/HostSync.cs              preserva CustomName
src/WRApperture.Client.Core/Launch/RdpFileBuilder.cs usa Name
src/WRApperture.Client/ViewModels/*                  AppTileViewModel.Name/ToolTip; Rename/ResetName/OpenSelected
src/WRApperture.Client/Views/MainWindow.xaml(.cs)    ListBox com seleção, duplo clique, Enter, F2
src/WRApperture.Client/Views/RenameAppWindow.xaml    NOVO
tests/WRApperture.Client.Tests/*                     nome local: modelo, sync, .rdp, persistência
```
