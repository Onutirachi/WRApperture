# Feature Specification: Host Manager — interface gráfica do PC host

**Feature Branch**: `20261007-135638-host-manager`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Fazer uma interface pro host pra facilitar a adição e remoção de apps. Se adicionar um atalho, ele pega o path e nome do atalho e, se possível, os argumentos de inicialização. Deve funcionar com executáveis diretos também, pegando o caminho e o nome automaticamente."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Publicar arrastando um atalho ou programa (Priority: P1)

O administrador arrasta para a janela um atalho (da área de trabalho ou do Menu Iniciar) ou um executável. O formulário já vem preenchido com nome, caminho, argumentos e ícone, e com um clique o app fica publicado.

**Why this priority**: É o principal ganho sobre a linha de comando: publicar sem digitar caminhos.

**Independent Test**: Arrastar o atalho do Bloco de Notas e o `calc.exe` para a janela, confirmar cada um e ver os dois na lista, abrindo-os pelo cliente.

**Acceptance Scenarios**:

1. **Given** um atalho com argumentos (ex.: um navegador com `--profile`), **When** é arrastado, **Then** o formulário mostra o nome do atalho, o executável de destino, os argumentos e o ícone do atalho.
2. **Given** um executável, **When** é arrastado, **Then** o nome vem da descrição do programa (ou do nome do arquivo) e o ícone vem do próprio executável.
3. **Given** vários itens arrastados de uma vez, **When** soltos, **Then** cada um é aberto em sequência para confirmação.
4. **Given** um arquivo que não é programa nem atalho de programa (ex.: um `.pdf` ou um atalho quebrado), **When** é arrastado, **Then** uma mensagem clara explica o motivo e nada é publicado.
5. **Given** o mesmo programa já publicado, **When** é arrastado de novo, **Then** o app existente é atualizado em vez de duplicado.

---

### User Story 2 - Ver, editar e remover apps publicados (Priority: P1)

A janela lista os apps publicados com ícone, nome e caminho. O administrador pode renomear, trocar os argumentos ou remover um app.

**Why this priority**: Gerenciar o que já está publicado é a outra metade do uso diário.

**Independent Test**: Renomear um app e ver o novo nome no cliente após atualizar. Remover e ver que ele some do cliente.

**Acceptance Scenarios**:

1. **Given** apps publicados (inclusive os criados fora da interface), **When** a janela abre, **Then** todos aparecem com ícone, nome, caminho e argumentos.
2. **Given** um app, **When** o administrador o edita e salva, **Then** as alterações valem para a próxima abertura no cliente.
3. **Given** um app, **When** o administrador remove e confirma, **Then** ele some da lista e do cliente.

---

### User Story 3 - Publicar a partir dos apps instalados (Priority: P2)

O administrador escolhe "Do Menu Iniciar", busca por nome, marca vários apps e publica todos de uma vez.

**Independent Test**: Buscar "calc", marcar a Calculadora e o Paint, publicar, e ver os dois na lista.

**Acceptance Scenarios**:

1. **Given** a lista de apps instalados, **When** o administrador digita na busca, **Then** só os apps correspondentes aparecem. Os já publicados aparecem marcados como tal.
2. **Given** vários apps marcados, **When** publica, **Then** todos entram na lista com os dados do atalho.

---

### User Story 4 - Estado do PC e pareamento (Priority: P1)

Um painel mostra se o PC está pronto para RemoteApp, se o agente está rodando, os endereços do PC, a porta e o fingerprint. Dele é possível preparar o PC, gerar um novo token (com botão de copiar) e reiniciar o agente.

**Why this priority**: Substitui `wrahost check`, `prepare`, `agent show` e `agent token`, as tarefas que hoje exigem terminal.

**Independent Test**: Gerar um novo token na interface, copiar e parear um cliente com ele.

**Acceptance Scenarios**:

1. **Given** um item de preparação pendente, **When** a janela abre, **Then** ele aparece destacado, com o botão "Preparar este PC".
2. **Given** o agente parado, **When** a janela abre, **Then** o estado "parado" aparece com o botão para iniciar ou reiniciar.
3. **Given** "Gerar novo token", **When** confirmado, **Then** o token aparece uma única vez com o botão "Copiar" e o aviso de que os clientes atuais precisarão do novo token.

---

### User Story 5 - Funcionar sem ser administrador (Priority: P2)

Aberta sem privilégios, a janela pede elevação. Se ela for recusada, a janela abre só para leitura, com um aviso e um botão "Reabrir como administrador".

### Edge Cases

- Atalho MSI "anunciado" (ex.: Office antigo): o destino real é resolvido, como na descoberta.
- Executável em pasta gravável por usuários comuns: publica, mas mostra o aviso de segurança.
- Atalho para `.bat`, `.cmd` ou URL: recusado com explicação (só programas `.exe` podem ser publicados).
- Nome muito longo ou com caracteres especiais: o alias é gerado automaticamente e com segurança.

## Requirements *(mandatory)*

- **FR-001**: A janela MUST aceitar arrastar e soltar atalhos `.lnk` e executáveis `.exe`, um ou vários, e também a seleção pelo botão "Adicionar…".
- **FR-002**: De um atalho, o sistema MUST extrair nome (nome do atalho), executável de destino (incluindo atalhos MSI), argumentos e ícone. De um executável, o nome vem da descrição do arquivo, com o nome do arquivo como alternativa.
- **FR-003**: Antes de publicar, o administrador MUST poder revisar e editar nome, caminho, argumentos e política de argumentos.
- **FR-004**: A janela MUST listar, editar e remover os apps publicados, inclusive os não criados por ela.
- **FR-005**: A janela MUST oferecer a publicação em lote a partir dos apps instalados, com busca.
- **FR-006**: A janela MUST mostrar o estado de preparação, o estado do agente, os endereços IPv4, a porta, o pareamento e o fingerprint, e permitir preparar o PC, gerar um novo token (exibido uma vez, copiável) e iniciar ou reiniciar o agente.
- **FR-007**: Sem administrador, a janela MUST pedir elevação e, se recusada, funcionar em modo somente leitura.
- **FR-008**: Toda alteração MUST passar pelo Core (constituição II) e ser registrada em log.
- **FR-009**: O instalador do host MUST criar o atalho "WRApperture (Host)" no Menu Iniciar de todos os usuários, e o desinstalador MUST removê-lo.

### Key Entities

- **App Draft**: dados extraídos de um atalho ou executável (nome, executável, argumentos, ícone) para revisão antes de publicar.

## Success Criteria *(mandatory)*

- **SC-001**: Publicar um app arrastando o atalho leva menos de 15 segundos, sem digitar nada.
- **SC-002**: O uso diário do host (publicar, remover, preparar, gerar token) não exige terminal.
- **SC-003**: 100% dos atalhos de programas do Menu Iniciar que a descoberta reconhece também são aceitos por arrastar.

## Assumptions

- A interface roda no próprio PC host, aberta por um administrador local.
- O tema escuro e a interface bilíngue ficam para os itens próprios do backlog.
