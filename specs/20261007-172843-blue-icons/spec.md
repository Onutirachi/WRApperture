# Feature Specification: Ícones azuis e atalhos recriados com o ícone novo

**Feature Branch**: `20261007-172843-blue-icons`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Refiz os ícones, simplifiquei um pouco e deixei azul … pode adaptá-los? E ajustar o ícone dos atalhos também, fiz um SVG para eles. Eu tinha criado um atalho antes da feature do símbolo e, mesmo recriando, ele não vem com o símbolo."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Novos desenhos (Priority: P1)

Os ícones do cliente e do host passam a ser as janelas azuis redesenhadas. O selo dos atalhos é o disco azul com o obturador, desenhado pelo autor, com um anel branco fino para não sumir sobre ícones azuis.

### User Story 2 - Recriar um atalho atualiza o ícone (Priority: P1)

**Acceptance Scenarios**:

1. **Given** um atalho criado antes do selo, **When** o usuário o cria de novo pelo cliente, **Then** o ícone aparece com o selo.

## Causa e correção

O cliente gravava o ícone sempre em `<alias>.ico`. O Windows guarda os ícones de atalhos em cache pelo caminho do arquivo, então um atalho recriado continuava com a imagem antiga.

Agora o arquivo é `<alias>-<hash do conteúdo>.ico` (`IconStore.WriteIco`): um ícone diferente sempre ganha um caminho novo. Além disso, `SHChangeNotify(SHCNE_UPDATEITEM)` avisa o Explorer de que o atalho mudou. As versões antigas dos arquivos ficam, porque outros atalhos podem usá-las, e saem com os dados do usuário na desinstalação.
