# Implementation Plan: Tema escuro

**Branch**: `20261007-152301-dark-theme` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

## Summary

- **`WRApperture.Ui`**:
  - `Theme.xaml` passa a ter só estilos e carrega `Colors.Light.xaml` como base. `Colors.Dark.xaml` tem as mesmas chaves e também fixa as `SystemColors`.
  - Os controles padrão que têm cores claras fixas no tema Aero2 (`ComboBox`, `ContextMenu`, separador de menu, `ToolTip`, `ScrollBar`) ganham templates próprios e enxutos.
- **`ThemeManager`**:
  - acrescenta ou remove `Colors.Dark.xaml` no fim dos dicionários do app (o último vence);
  - aplica a barra de título escura em toda janela que carrega (class handler de `Loaded`);
  - escuta `SystemEvents.UserPreferenceChanged`;
  - guarda a preferência no HKCU.
- **`ThemeMenu`**: cria os três itens de tema em qualquer menu (o ⋯ do cliente e o ◐ do app do host).
- **Telas**: `StaticResource` vira `DynamicResource` nas cores, e as cores fixas (erro, sucesso, sobreposição) viram as chaves `Error`, `Success` e `Overlay`.

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | Sem pacotes novos |
| II. Registro via Core | ✅ | Só uma preferência de interface no HKCU do usuário; nada de RemoteApp |
| III. Idempotente | ✅ | Aplicar o mesmo tema de novo não muda nada |
| IV. Segurança | ✅ | Sem dados sensíveis |
| V. Observabilidade | ✅ | — |

## Validação

Janela do app do host em `--read-only` com o tema escuro, capturada com `PrintWindow`, que desenha só aquela janela.
