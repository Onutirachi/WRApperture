# Feature Specification: Ícone da "Área de trabalho"

**Feature Branch**: `20261007-185944-desktop-icon`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "O ícone quando cria o atalho da área de trabalho fica com resolução ruim … consegue trocar o ícone da interface da área de trabalho, do atalho gerado da área de trabalho e da bandeja da área de trabalho pra ser só o círculo, igual o símbolo do atalho?"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - O círculo em todo lugar (Priority: P1)

A "Área de trabalho" usa o círculo azul com o obturador, desenhado pelo autor, em três lugares:

- o bloco da grade;
- o item da bandeja;
- o atalho criado na área de trabalho ou no Menu Iniciar.

Diferente do selo, este ícone não tem o anel branco, porque aparece sozinho.

### User Story 2 - Nítido em qualquer tamanho (Priority: P1)

O ícone do atalho é desenhado em vetor em cada tamanho do `.ico` (16 a 256 px, incluindo os de 125%, 150% e 200%), em vez de reduzir uma imagem de 256 px.

## Requirements *(mandatory)*

- **FR-001**: `assets/icons/desktop.svg` gera, via `scripts/build-icons.ps1`, o `desktop.xaml` (vetor, embutido no cliente) e o `desktop.png` (prévia).
- **FR-002**: `Artwork` carrega os desenhos vetoriais (`badge`, `desktop`) e os desenha em qualquer tamanho. O `client.png`, que servia de base para o atalho, foi removido.
