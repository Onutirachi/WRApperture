# Feature Specification: Área de trabalho completa do host

**Feature Branch**: `20261007-150633-full-desktop`

**Created**: 2026-10-07

**Status**: Draft

**Input**: Item 2 do backlog: "opção de abrir a área de trabalho completa do host, como padrão ou atalho". Formato escolhido: um bloco fixo "Área de trabalho" na grade de cada host.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Abrir a área de trabalho do PC remoto (Priority: P1)

Além dos apps, a grade mostra no início um bloco "Área de trabalho". Com duplo clique (ou Enter), o cliente abre a área de trabalho inteira do host em tela cheia, com o mesmo pareamento, credenciais e assinatura dos apps.

**Why this priority**: Às vezes é preciso algo que não foi publicado (configurações, um arquivo, um programa ocasional) e hoje isso exige abrir a Conexão de Área de Trabalho Remota à parte.

**Independent Test**: Duplo clique em "Área de trabalho": abre a área de trabalho do host em tela cheia, sem o alerta de segurança (com a assinatura configurada) e sem pedir senha (com credenciais salvas).

**Acceptance Scenarios**:

1. **Given** um host pareado, **When** a grade é exibida, **Then** o primeiro bloco é "Área de trabalho", com um ícone próprio, mesmo antes de existir algum app publicado.
2. **Given** o bloco, **When** o usuário dá duplo clique ou pressiona Enter, **Then** a área de trabalho do host abre em tela cheia.
3. **Given** o bloco, **When** o usuário escolhe "Criar atalho na área de trabalho", **Then** surge um atalho "<host> — Área de trabalho" que abre a mesma conexão.
4. **Given** o bloco, **When** o usuário clica com o botão direito, **Then** "Renomear…" não aparece (o nome é fixo).

---

### User Story 2 - Sem confirmações nas duas pontas (Priority: P1)

Como nos apps, o cliente prepara o host antes de conectar, mas com a regra da área de trabalho.

**Acceptance Scenarios**:

1. **Given** o mesmo usuário logado no host (sessão de área de trabalho), **When** a área de trabalho é aberta, **Then** a sessão existente é reaproveitada, com os programas abertos, sem encerrar nada.
2. **Given** apps remotos do mesmo usuário abertos (sessão RemoteApp), **When** a área de trabalho é aberta, **Then** essa sessão é encerrada antes (senão a área de trabalho abriria sem a barra de tarefas), com o mesmo aviso e opção "Perguntar antes" dos apps.
3. **Given** outro usuário usando o host, **When** a área de trabalho é aberta, **Then** a sessão dele é desconectada (programas continuam abertos), como nos apps.
4. **Given** um host com agente anterior a esta versão, **When** a área de trabalho é aberta, **Then** o cliente não pede ao agente para liberar nada (a regra antiga encerraria a própria sessão do usuário) e o Windows decide como sempre.

### Edge Cases

- Usuário RDP desconhecido (credenciais não salvas): sessões RemoteApp ativas são consideradas do próprio usuário (são criadas pelo WRApperture) e encerradas; sessões de área de trabalho ativas são consideradas dele e reaproveitadas.
- A busca filtra o bloco pelo nome, como os apps.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O cliente gera um `.rdp` de área de trabalho por host (`remoteapplicationmode:i:0`, tela cheia, resolução dinâmica), com o mesmo endereço, usuário e assinatura dos apps.
- **FR-002**: A API de sessão aceita o modo `desktop` (`GET /api/session?mode=desktop` e `mode` no corpo do `POST /api/session/release`). Sem modo, vale a regra atual (`remoteapp`).
- **FR-003**: Regra `desktop`: encerra as sessões RemoteApp do usuário, desconecta as sessões ativas de outros usuários e nunca encerra sessões de área de trabalho do próprio usuário.
- **FR-004**: `GET /api/host` informa `features: ["desktop-session"]`. O cliente guarda isso na sincronização e só usa o modo `desktop` com agentes que o anunciam.

## Success Criteria *(mandatory)*

- **SC-001**: A área de trabalho abre com no máximo um duplo clique, sem diálogos extras do WRApperture no caso comum.
- **SC-002**: Nenhum programa aberto na sessão de área de trabalho do próprio usuário é fechado ao abrir a área de trabalho.
