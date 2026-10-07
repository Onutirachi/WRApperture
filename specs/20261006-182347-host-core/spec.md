# Feature Specification: Host Core — publicação de RemoteApps

**Feature Branch**: `20261006-182347-host-core`

**Created**: 2026-10-06

**Status**: Draft

**Input**: User description: "Biblioteca Core do host: publicar apps RemoteApp no registro, preparar o host para aceitar conexões RemoteApp e descobrir apps instalados, para que outro computador abra apps individuais sem acessar a área de trabalho inteira."

## User Scenarios & Testing *(mandatory)*

> Esta feature é a base usada pelo Manager (GUI) e pelo Agent (API). Os "usuários" aqui são o administrador do PC host e os componentes que consomem a biblioteca. Os cenários descrevem o comportamento observável, independente da interface.

### User Story 1 - Publicar e despublicar um app (Priority: P1)

O administrador do host escolhe um executável (por exemplo, Bloco de Notas ou Excel), dá um nome amigável e publica. A partir daí, um cliente RDP em modo RemoteApp que pedir esse app pelo alias abre só a janela dele. O administrador também pode remover a publicação.

**Why this priority**: É o núcleo do produto. Sem isso, nenhum app fica acessível remotamente.

**Independent Test**: Publicar `C:\Windows\System32\notepad.exe` com alias `notepad`, depois abrir `mstsc` com um `.rdp` em modo RemoteApp apontando para `||notepad` a partir de outra máquina e confirmar que só o Bloco de Notas aparece. Despublicar e confirmar que a conexão é recusada.

**Acceptance Scenarios**:

1. **Given** um executável existente, **When** o administrador publica com nome "Bloco de Notas", **Then** o app aparece na lista de publicados com alias único, nome, caminho e ícone.
2. **Given** um app já publicado, **When** o mesmo app é publicado de novo com os mesmos dados, **Then** nada é duplicado e a operação conclui com sucesso (idempotente).
3. **Given** um app publicado, **When** o administrador o despublica, **Then** ele some da lista e as outras publicações continuam intactas.
4. **Given** um caminho de executável inexistente, **When** o administrador tenta publicar, **Then** a operação é recusada com mensagem clara e nada é gravado.
5. **Given** um app publicado, **When** o administrador edita nome, ícone ou argumentos de linha de comando, **Then** as alterações passam a valer na próxima abertura do app.

---

### User Story 2 - Preparar o host para RemoteApp (Priority: P1)

O administrador verifica se o PC está pronto para servir RemoteApps (Área de Trabalho Remota habilitada, firewall liberado, autenticação em nível de rede ativa, lista de apps permitidos ativa) e, com uma ação, corrige o que estiver faltando.

**Why this priority**: Em um Windows Pro "de fábrica", publicar apps não basta. Sem preparar o host, a conexão falha, e esse é o principal ponto de atrito hoje.

**Independent Test**: Em uma VM com Área de Trabalho Remota desabilitada, rodar o diagnóstico (deve listar os itens pendentes), aplicar a preparação, rodar o diagnóstico de novo (tudo OK) e conectar com sucesso.

**Acceptance Scenarios**:

1. **Given** um host com Área de Trabalho Remota desabilitada, **When** o diagnóstico roda, **Then** o item "RDP habilitado" aparece como pendente, com descrição do que será alterado.
2. **Given** um host com pendências, **When** a preparação é aplicada, **Then** todos os itens ficam OK e uma segunda aplicação não altera nada.
3. **Given** um host preparado, **When** o administrador pede para reverter, **Then** apenas as configurações alteradas pela ferramenta voltam ao estado anterior registrado.
4. **Given** um Windows Home (sem servidor RDP), **When** o diagnóstico roda, **Then** ele informa que a edição não é suportada e não tenta aplicar nada.

---

### User Story 3 - Descobrir apps instalados (Priority: P2)

Em vez de procurar o `.exe` manualmente, o administrador vê a lista de apps instalados no host (como no Menu Iniciar), com nome e ícone, e escolhe quais publicar.

**Why this priority**: Facilita muito o uso, mas publicar por caminho manual (US1) já entrega valor sozinho.

**Independent Test**: Chamar a descoberta em um host com Office instalado e confirmar que "Excel", "Word" etc. aparecem com nome, caminho do executável e ícone, sem duplicatas.

**Acceptance Scenarios**:

1. **Given** apps com atalho no Menu Iniciar (de todos os usuários e do usuário atual), **When** a descoberta roda, **Then** cada app aparece uma vez, com nome de exibição, caminho do executável e ícone.
2. **Given** atalhos quebrados (alvo inexistente) ou que apontam para desinstaladores, documentos ou URLs, **When** a descoberta roda, **Then** esses itens são ignorados.
3. **Given** um app já publicado, **When** a descoberta roda, **Then** ele vem marcado como "já publicado".

---

### User Story 4 - Obter ícone de um app (Priority: P2)

Os componentes (Manager e Agent) precisam do ícone de cada app publicado em formato de imagem para exibir ao usuário e enviar ao cliente.

**Why this priority**: Um cliente com ícones é muito mais usável, mas não é bloqueante para abrir apps.

**Independent Test**: Pedir o ícone de `notepad` e receber uma imagem PNG válida de pelo menos 32x32.

**Acceptance Scenarios**:

1. **Given** um app publicado com ícone definido, **When** o ícone é solicitado, **Then** uma imagem PNG é retornada.
2. **Given** um executável sem ícone embutido, **When** o ícone é solicitado, **Then** um ícone genérico padrão é retornado.

### Edge Cases

- Alias com caracteres inválidos para chave de registro (`\`, espaço, acentos): o sistema gera um alias seguro a partir do nome e, em caso de colisão, acrescenta um sufixo numérico.
- Executável em caminho com espaços ou variáveis de ambiente (`%ProgramFiles%`): é aceito e expandido na validação.
- Executável em pasta gravável por usuário comum (ex.: `%LOCALAPPDATA%`, `Downloads`): a publicação é permitida, mas retorna um aviso de segurança.
- A ferramenta roda sem privilégios de administrador: as operações de escrita falham com um erro explícito de "requer administrador", e as de leitura continuam funcionando.
- Já existem publicações criadas manualmente ou por outra ferramenta na lista de permitidos: elas são listadas e preservadas. Nunca são apagadas em lote.
- App UWP/Store (sem `.exe` tradicional): fora do escopo da v1. A descoberta não o lista.
- A política "permitir qualquer programa sem lista" (fDisabledAllowList) está ligada: o diagnóstico sinaliza como risco e a preparação a desliga.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O sistema MUST listar todos os apps publicados como RemoteApp no host, incluindo os criados fora da ferramenta.
- **FR-002**: O sistema MUST publicar um app a partir de: caminho do executável, nome de exibição, alias opcional, argumentos opcionais e ícone opcional (caminho + índice).
- **FR-003**: O sistema MUST validar, antes de gravar, que o executável existe e que o caminho é absoluto (após expandir variáveis de ambiente).
- **FR-004**: O sistema MUST gerar um alias único e seguro quando não informado, e rejeitar um alias informado que seja inválido.
- **FR-005**: Publicar, atualizar e despublicar MUST ser operações idempotentes e afetar apenas o app alvo.
- **FR-006**: O sistema MUST permitir definir, por app, a política de argumentos: "nenhum", "qualquer um enviado pelo cliente" ou "somente os argumentos fixos definidos".
- **FR-007**: O sistema MUST diagnosticar a prontidão do host com estes itens: edição do Windows suportada, Área de Trabalho Remota habilitada, regra de firewall de RDP ativa, autenticação em nível de rede ativa, lista de apps permitidos aplicada (não ignorada).
- **FR-008**: O sistema MUST aplicar as correções do diagnóstico de forma idempotente, registrando o estado anterior de cada item alterado.
- **FR-009**: O sistema MUST reverter a preparação usando o estado anterior registrado, sem tocar em itens que não alterou.
- **FR-010**: O sistema MUST descobrir apps instalados a partir dos atalhos do Menu Iniciar (todos os usuários e usuário atual), descartando atalhos quebrados, desinstaladores e alvos que não são executáveis.
- **FR-011**: O sistema MUST fornecer o ícone de qualquer app (publicado ou descoberto) como imagem PNG, com fallback para um ícone genérico.
- **FR-012**: O sistema MUST emitir aviso (sem bloquear) quando o executável estiver em pasta gravável por usuário não administrador.
- **FR-013**: Operações de escrita MUST falhar com erro explícito quando o processo não tiver privilégio de administrador.
- **FR-014**: O sistema MUST registrar em log estruturado toda alteração de configuração (o que mudou, valor anterior, valor novo, resultado).

### Key Entities

- **Published App**: app exposto como RemoteApp. Atributos: alias (identificador único), nome de exibição, caminho do executável, ícone (caminho + índice), política de argumentos, argumentos fixos, visível ou oculto para listagem web.
- **Discovered App**: app encontrado no host e candidato a publicação. Atributos: nome de exibição, caminho do executável, argumentos do atalho, origem (todos os usuários ou usuário atual), ícone, flag "já publicado".
- **Host Readiness Check**: item de diagnóstico. Atributos: identificador, descrição, estado (OK, pendente, não suportado), valor atual, valor esperado.
- **Preparation Snapshot**: registro do estado anterior dos itens alterados pela preparação, usado na reversão.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um administrador publica um app e o abre remotamente pela primeira vez em menos de 2 minutos, sem editar o registro manualmente.
- **SC-002**: Em um Windows 10/11 Pro recém-instalado, a preparação deixa o host pronto para RemoteApp com uma única ação, e 100% dos itens do diagnóstico ficam OK.
- **SC-003**: Repetir qualquer operação (publicar, preparar, despublicar) 10 vezes seguidas produz o mesmo estado final que executá-la uma vez.
- **SC-004**: A descoberta lista pelo menos 95% dos apps que aparecem em "Todos os apps" do Menu Iniciar (excluindo apps da Store), sem duplicatas, em menos de 5 segundos.
- **SC-005**: Nenhuma publicação pré-existente criada fora da ferramenta é perdida ou alterada por qualquer operação da ferramenta.

## Assumptions

- O host roda Windows 10 ou 11 Pro, Enterprise ou Education. Home não é suportado, porque não tem servidor RDP.
- O host aceita uma única sessão RDP por vez (limitação do Windows cliente). Abrir um RemoteApp desconecta quem estiver logado localmente. Isso é documentado, não contornado. Vários apps simultâneos do mesmo cliente e usuário compartilham essa única sessão (cada um em janela própria). Um segundo cliente ou outro usuário toma a sessão.
- O usuário que conecta pelo cliente tem conta no host com permissão de Área de Trabalho Remota (administradores ou grupo "Usuários da Área de Trabalho Remota"). O gerenciamento desses grupos fica fora desta feature.
- Apps UWP/Store ficam fora do escopo da v1.
- A fonte da verdade dos apps publicados é a própria configuração do Windows. Não existe banco de dados separado.
- A interface gráfica (Manager) e a API (Agent) são features separadas que consomem esta.
