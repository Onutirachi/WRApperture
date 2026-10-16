# Feature Specification: Novo layout do app do host

**Feature Branch**: `20261016-143103-host-layout`

**Created**: 2026-10-16

**Status**: Draft

**Input**:

- "Pode fazer na 0.20 mesmo. Tenta fazer um estilo parecido com o cliente, sem as bordas dos itens e tudo mais, botões de pílula, tela de configuração."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mesmo visual do cliente (Priority: P1)

O app do host ganha o mesmo layout do cliente:

- **Barra lateral sobre o fundo Mica,** com o nome do PC e as páginas:
  - Apps publicados;
  - Este PC: tem um ponto amarelo quando algo impede o acesso remoto;
  - Conexão;
  - Configurações, no rodapé.
- **A página aberta fica numa camada translúcida,** com título grande. Nada tem borda: os itens são cartões arredondados e os botões são pílulas.
- **Apps publicados:**
  - "Adicionar programa ou atalho…" é o botão principal, e "Do Menu Iniciar…" fica ao lado.
  - Editar e Remover viram ícones.
  - Cada app é um cartão. Clique duplo edita, e o botão direito mostra Editar e Remover.
- **Este PC:** cada item é um cartão com ícone de estado (ok, pendente, aviso ou não suportado) e o botão de resolver ao lado. "Resolver tudo" fica no título.
- **Conexão:**
  - Os dados do agente ficam num cartão, com "Reiniciar agente" e "Token manual…".
  - Cada dispositivo pareado é um cartão com o ícone de remover.
- **Configurações:**
  - **Aparência:** o tema (seguir o Windows, claro ou escuro) e o idioma.
  - **Atualizações:** a versão, a última versão, a atualização automática e "Atualizar agora".
  - Substituem o antigo botão "◐".
- **A barra de status fica no rodapé,** com a versão à direita.

### User Story 2 - Janelas menores sem bordas (Priority: P3)

Os quadros das janelas menores perdem a borda e ficam com cantos arredondados:

- no host: aprovação de pareamento, token, senha e descoberta;
- no cliente: adicionar host, credenciais e liberar sessão.

## Requisitos e decisões

- **Tema compartilhado:** os estilos `NavItem` (página da barra lateral) e `Card` (quadro de uma página) ficam no `Theme.xaml`, junto dos demais.
- **Só muda o visual:** comandos, permissões e o modo somente leitura continuam iguais.
