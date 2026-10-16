# Feature Specification: Controle remoto do host pelo cliente

**Feature Branch**: `20261016-175109-remote-control`

**Created**: 2026-10-16

**Status**: Draft

**Input**:

- "Um botão junto com a Área de trabalho pra acessar a interface do host pelo client. Eu adicionei aqui e funciona bem."
- "Um botão para encerrar a sessão do RemoteApp, fechar todos os apps. Pra fazer isso, tenho que acessar a área de trabalho."
- "Mostrar uma tagzinha no ícone do app dizendo que ele tá aberto, e ter uma opção de matar o app lá no host pelo client, clicando com o botão direito."
- "Achei um bug que fez o FortiClient não funcionar. Pra voltar a funcionar, tive que acessar a área de trabalho pra forçar o fechamento do RemoteApp."
- "Pode ser Gerenciar host. Pode ser os dois, Fechar e Encerrar tarefa."
- Mesma versão, branch `20261016-174729-client-toolbar`: botão único de grade/lista, busca que encolhe, clicar no vazio tira a seleção e a marca d'água do portal na área de apps (cliente e host).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Gerenciar host (Priority: P1)

O botão fixo "Gerenciar host" fica ao lado de "Área de trabalho", com as setas do ícone.

- Ele abre o app do host (WRApperture.Host.exe) como RemoteApp. O Windows pede administrador dentro da janela remota.
- O agente publica esse app sozinho, oculto da lista de apps e do próprio app do host.
- Se o WRApperture.Host.exe já tiver sido publicado à mão, essa entrada é usada como está.
- O desinstalador do host remove a entrada criada pelo agente.

### User Story 2 - Encerrar a sessão remota (Priority: P1)

O botão ⏻ ("Encerrar sessão remota") desconecta a sessão RemoteApp deste computador no host. Todos os apps remotos fecham, inclusive os que abriram junto, como o FortiTray.

- Antes de encerrar, o cliente pede confirmação: o que não estiver salvo nesses apps é perdido.
- A ação também está no menu ⋯ do host e no ícone da bandeja.
- Sessões de área de trabalho nunca são tocadas, incluindo a de quem usa o PC.

### User Story 3 - Apps abertos (Priority: P2)

Um ponto verde no ícone marca o app aberto no host. Com a atualização em tempo real, o ponto aparece e some em poucos segundos.

O botão direito num app aberto mostra:
- **Fechar:** como o X da janela. O app pode perguntar se você quer salvar. O agente roda `taskkill /PID …`, sem `/F`, na sessão do app.
- **Encerrar tarefa:** força o fechamento, com confirmação.

## Requisitos e decisões

- **App aberto:** é um processo cujo caminho é igual ao executável publicado, numa sessão RemoteApp. Apps abertos por um lançador que inicia outro .exe e fecha não são reconhecidos.
- **Contrato:**
  - recurso `remote-control`;
  - `HostInfoDto.ManagerAlias` e `AppSummaryDto.Running`;
  - `POST /api/apps/{alias}/close`, `POST /api/apps/{alias}/kill` → `AppStopResultDto`;
  - `POST /api/session/end` → `SessionEndResultDto`.
  - Detalhes no contrato da API em `specs/20261007-105635-host-agent/contracts/http-api.md`.
- **Hosts anteriores à 0.21.0:** não mostram os botões, o ponto nem o menu.
- **Janela estreita:** "Área de trabalho" e "Gerenciar host" ficam só com o ícone. O nome continua na dica.
