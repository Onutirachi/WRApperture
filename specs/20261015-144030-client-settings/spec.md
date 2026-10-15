# Feature Specification: Menus do cliente, versões e sincronização automática

**Feature Branch**: `20261015-144030-client-settings`

**Created**: 2026-10-15

**Status**: Draft

**Input**:

- "O menuzinho de 3 pontinhos tem coisas misturadas, sobre o host e sobre a configuração do WRApperture. Seria ideal separar o que é do host nos 3 pontinhos e criar um botão engrenagem com o resto (bandeja, inicialização, tema, idioma etc.)."
- "O alerta de host desatualizado só apareceu quando tentei acessar um app; deveria ter aparecido quando cliquei para verificar atualização."
- "Não tem como ver que versão estou usando no cliente. Queria ver a versão do host pelo cliente."
- "O botão atualizar é pouco elegante, teria como fazer ele atualizar sozinho?"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cada coisa no seu menu (Priority: P1)

- **⋯ Opções do host**, só com o que é do host selecionado:
  - "PC-SALA · versão 0.17.0" no topo;
  - salvar e apagar credenciais;
  - perguntar antes de encerrar a sessão;
  - **Sincronizar agora** (F5);
  - **Atualizar o PC remoto**, só quando ele estiver desatualizado;
  - remover host.
- **⚙ Configurações**, com o que vale para este computador:
  - abrir apps sem alerta;
  - fechar para a bandeja;
  - iniciar com o Windows;
  - **Tema** (submenu);
  - idioma;
  - atualizar automaticamente;
  - verificar atualizações;
  - "WRApperture 0.17.0" no rodapé.
- A ⚙ funciona mesmo sem nenhum host.

### User Story 2 - A lista se atualiza sozinha (Priority: P1)

- O botão **⟳ Atualizar** sai da barra.
- A lista do host selecionado sincroniza:
  - ao trocar de host;
  - a cada 5 minutos;
  - ao voltar para a janela, se a última sincronização tiver mais de 1 minuto.
- As sincronizações automáticas nunca abrem janelas: token revogado ou certificado trocado aparecem só na barra de status. **Sincronizar agora** e F5 continuam perguntando o que fazer.

### User Story 3 - Host desatualizado é percebido na hora (Priority: P1)

- A primeira verificação de atualizações roda 10 segundos depois de abrir, e não mais 1 minuto. Assim o cliente que acabou de se atualizar logo percebe que o host ficou para trás.
- **Verificar atualizações** sincroniza o host antes, para comparar com a versão atual dele, e não com a da última sincronização.
- Com a atualização automática ligada no cliente **e** no host (`GET /api/update` → `autoUpdate`), o cliente pede ao host que se atualize sozinho, uma vez por execução. Se o host estiver com a atualização automática desligada, só aparece o aviso, como antes.

## Notas

- `IAgentApi.GetHostUpdateStatusAsync` lê `GET /api/update`, que já existe desde a 0.15.0.
- `ThemeMenu.AddTo` também funciona num submenu (`MenuItem`).
