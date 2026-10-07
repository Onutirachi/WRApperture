# Feature Specification: Vários hosts no dia a dia, selo nítido e ícones na bandeja

**Feature Branch**: `20261007-175726-multi-host-polish`

**Created**: 2026-10-07

**Status**: Draft

**Input**:

- "Como funciona se eu tiver mais de um host e quiser usar apps de diferentes hosts ao mesmo tempo? … seria bom criar os atalhos no desktop com nomedoapp - nomedohost … se eu tiver o mesmo app em 2 hosts diferentes, preciso conseguir distinguir."
- "A resolução do iconezinho azul que fica nos atalhos criados é bem ruim."
- "Na bandeja, o Área de trabalho ficou sem ícone."
- "Consegue fazer submenus por host com os apps dentro?"
- "Não precisa o nome do host na interface, só nos atalhos criados."

## Contexto: vários hosts ao mesmo tempo

Isso já funcionava antes desta feature. Cada host tem o próprio endereço, e o `mstsc` abre uma sessão RDP separada para cada um, então apps de hosts diferentes ficam abertos lado a lado.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Atalhos distinguem o host (Priority: P1)

Atalhos criados pelo cliente (área de trabalho e Menu Iniciar) se chamam "<app> - <host>". Exemplos: "Bloco de Notas - PC-ESCRITORIO" e "Área de trabalho - PC-CASA". A grade e a bandeja continuam mostrando só o nome do app.

### User Story 2 - Bandeja com vários hosts (Priority: P1)

Com mais de um host, o menu da bandeja tem um submenu por host (o selecionado em negrito), com a "Área de trabalho" e os apps dentro. Clicar num app de outro host seleciona esse host e abre o app. Com um único host, os apps continuam direto no menu.

### User Story 3 - Selo e ícones nítidos (Priority: P2)

- O selo dos atalhos é desenhado em vetor (`assets/icons/badge.xaml`, gerado do `badge.svg`) no tamanho exato de cada ícone, em vez de reduzir uma imagem de 256 px.
- O `.ico` dos atalhos inclui os tamanhos usados com escala de 125%, 150% e 200%.
- Na bandeja, "Área de trabalho" e os apps sem ícone ganham o mesmo bloco azul da grade (monitor ou inicial).

## Notas

- Atalhos criados antes desta versão mantêm o nome antigo. Recriar gera o nome novo; o antigo pode ser apagado.
