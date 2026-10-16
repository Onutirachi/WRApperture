# Feature Specification: Nova tela do cliente

**Feature Branch**: `20261015-230032-client-layout`

**Created**: 2026-10-15

**Status**: Draft

**Input**: Desenhos do usuário no CorelDRAW, comparados com um print da tela atual.
- Barra lateral de hosts em cartões (nome, ⋯, versão, quantidade de apps, "Offline").
- ⚙ e + no rodapé da barra.
- Cabeçalho com o nome do host e a VPN.
- Busca, "Área de trabalho" e grade/lista.
- Rodapé com o status e a versão do cliente.
- "Seria legal poder escolher nome customizado para o host e uma cor para o cartão."
- "O menu de configurações seria legal se fosse uma tela à parte, ou substituir a principal com um botão de voltar."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Hosts em cartões (Priority: P1)

- A barra lateral substitui o seletor de host. Ela aparece mesmo com um host só, para a tela não mudar de forma ao adicionar o segundo.
- Cada cartão mostra:
  - o nome (o personalizado ou o do computador);
  - ⋯ com as opções do host;
  - a versão do host;
  - "N apps";
  - "Offline" e o nome acinzentado quando o host não respondeu na última sincronização;
  - à esquerda, uma faixa com a cor escolhida.
- O ⋯ de cada cartão seleciona aquele host e abre o menu dele:
  - Renomear…;
  - Cor ▸ (sem cor e 8 cores);
  - credenciais;
  - perguntar antes de encerrar a sessão;
  - Sincronizar agora;
  - Atualizar o PC remoto;
  - Remover host.
- O nome e a cor valem só neste computador e são guardados no `hosts.json`. Os atalhos de área de trabalho continuam usando o nome do computador.

### User Story 2 - Área do host (Priority: P1)

- O cabeçalho mostra o nome do host e, quando houver, a VPN ativa.
- Abaixo do cabeçalho:
  - a busca (Ctrl+F);
  - o botão **Área de trabalho**, que sai da grade (o botão direito nele cria atalhos para ela; a bandeja continua igual);
  - a escolha entre **blocos e lista**, que o cliente lembra.
- O rodapé mostra o status à esquerda e "WRApperture x.y.z" à direita.

### User Story 3 - Configurações (Priority: P2)

O ⚙ troca a área do host por uma página de configurações, com "← Voltar". A barra de hosts continua à vista. A página tem três seções:
- **Geral:** fechar para a bandeja, iniciar com o Windows, abrir apps sem alerta;
- **Aparência:** tema (seguir o Windows, claro ou escuro) e idioma;
- **Atualizações:** atualizar automaticamente, verificar agora e a versão.
