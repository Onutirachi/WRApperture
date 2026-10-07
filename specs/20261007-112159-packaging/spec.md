# Feature Specification: Packaging — instalação do host e do cliente

**Feature Branch**: `20261007-112159-packaging`

**Created**: 2026-10-07

**Status**: Draft

**Input**: User description: "Empacotar o projeto para uso real: o agente precisa rodar sozinho em segundo plano no PC host (iniciando com o Windows), com firewall configurado, e o cliente precisa ser fácil de instalar em outro PC, sem exigir .NET instalado."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Instalar o host com um comando (Priority: P1)

O administrador copia o pacote do host para o PC remoto e roda um único comando de instalação. Ao final, o PC está preparado para RemoteApp, o agente roda em segundo plano (e volta sozinho após reiniciar), a porta está liberada no firewall e o token de pareamento é exibido para ser informado no cliente.

**Why this priority**: Sem isso, o agente só funciona com um terminal aberto. É o que separa um protótipo de algo usável no dia a dia.

**Independent Test**: Numa VM Windows Pro limpa, rodar o instalador e reiniciar a VM. O agente deve responder em `/health` sem ninguém logado, e um cliente deve conseguir parear com o token exibido.

**Acceptance Scenarios**:

1. **Given** um PC Windows Pro sem o produto, **When** o administrador roda o instalador, **Then** o host fica preparado, o agente fica instalado como serviço automático e em execução, a porta é liberada no firewall e o token e o fingerprint são exibidos.
2. **Given** o host instalado, **When** o PC reinicia, **Then** o agente volta a responder sem intervenção.
3. **Given** o host já instalado, **When** o instalador roda de novo (por exemplo, para atualizar a versão), **Then** os binários são atualizados, o serviço é reiniciado, o token atual continua valendo (os clientes não precisam reparear) e os apps publicados são preservados.
4. **Given** um terminal sem privilégios de administrador, **When** o instalador roda, **Then** ele para imediatamente com mensagem clara, sem alterar nada.
5. **Given** o agente travar ou encerrar inesperadamente, **When** isso ocorrer, **Then** o Windows o reinicia automaticamente.

---

### User Story 2 - Desinstalar o host (Priority: P2)

O administrador remove o produto do host. Por padrão, os apps publicados e as configurações são mantidos, para facilitar uma reinstalação. Com a opção de limpeza completa, as alterações de preparação são desfeitas e as configurações e o certificado do agente são apagados.

**Why this priority**: Reversibilidade é princípio do projeto (constituição III), mas é menos frequente que instalar.

**Independent Test**: Instalar, desinstalar e verificar que o serviço, a regra de firewall e os arquivos sumiram. Repetir com limpeza completa e verificar que o diagnóstico volta ao estado original.

**Acceptance Scenarios**:

1. **Given** o host instalado, **When** o administrador desinstala, **Then** o serviço, a regra de firewall do agente e os arquivos do programa são removidos, e os apps publicados permanecem.
2. **Given** o host instalado, **When** o administrador desinstala com limpeza completa, **Then** a preparação do host é revertida e o token, as configurações do agente e o certificado são apagados.
3. **Given** o produto já desinstalado, **When** a desinstalação roda de novo, **Then** ela conclui sem erro (idempotente).

---

### User Story 3 - Instalar o cliente (Priority: P1)

O usuário do outro computador baixa o pacote do cliente e o instala sem privilégios de administrador e sem instalar .NET. Ele ganha um atalho no Menu Iniciar.

**Why this priority**: O cliente é a porta de entrada do usuário final.

**Independent Test**: Num PC sem .NET, rodar o instalador do cliente como usuário comum, abrir pelo Menu Iniciar e parear com um host.

**Acceptance Scenarios**:

1. **Given** um PC sem .NET, **When** o usuário instala o cliente, **Then** o app fica disponível no Menu Iniciar e abre normalmente.
2. **Given** o cliente instalado, **When** o usuário reinstala uma versão nova, **Then** os hosts pareados e as configurações são preservados.
3. **Given** o cliente instalado, **When** o usuário desinstala, **Then** o programa e o atalho somem. Os dados do usuário (hosts e ícones) só são apagados se ele pedir.

---

### User Story 4 - Gerar os pacotes de release (Priority: P1)

O desenvolvedor gera, com um comando, os pacotes do host e do cliente prontos para distribuir: executáveis autônomos, scripts de instalação e um arquivo compactado por pacote, com a versão no nome.

**Why this priority**: É o que produz os artefatos das US1 e US3.

**Independent Test**: Rodar o build de release e verificar que os dois pacotes contêm os executáveis esperados, que eles rodam num PC sem .NET e que o nome do arquivo contém a versão.

**Acceptance Scenarios**:

1. **Given** o código-fonte, **When** o build de release roda, **Then** são gerados `WRApperture-Host-<versão>.zip` e `WRApperture-Client-<versão>.zip`.
2. **Given** um teste falhando, **When** o build de release roda, **Then** ele é interrompido e nenhum pacote é gerado.

### Edge Cases

- A porta do agente foi alterada (`wrahost agent port`): a regra de firewall e o serviço usam a porta configurada.
- O host já tem outro serviço usando a porta 5080: o instalador detecta que o agente não respondeu, mostra a mensagem do log e sugere trocar a porta.
- Instalação a partir de pasta de rede ou de pasta temporária: os arquivos são copiados para `Program Files`. O serviço nunca aponta para a pasta de origem.
- Antivírus ou SmartScreen bloqueando o executável não assinado: o problema é documentado. Assinatura de código fica fora do escopo da v1.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O build de release MUST rodar os testes e gerar pacotes do host e do cliente com executáveis autônomos (sem exigir .NET instalado) e a versão no nome.
- **FR-002**: O instalador do host MUST exigir administrador e, antes de tudo, recusar com mensagem clara se não tiver.
- **FR-003**: O instalador do host MUST copiar os binários para uma pasta protegida (`Program Files`), preparar o host, registrar o agente como serviço automático com reinício em caso de falha, liberar a porta configurada no firewall e iniciar o serviço.
- **FR-004**: O instalador do host MUST gerar o token de pareamento apenas se o host ainda não estiver pareado, e exibi-lo junto com o fingerprint ao final.
- **FR-005**: O instalador do host MUST ser idempotente e servir também como atualização, preservando token, apps e configurações.
- **FR-006**: O instalador do host MUST verificar, ao final, que o agente responde, e indicar onde estão os logs se não responder.
- **FR-007**: O desinstalador do host MUST remover serviço, regra de firewall e arquivos, preservando apps e configurações por padrão. A limpeza completa reverte a preparação e apaga as configurações do agente e o certificado.
- **FR-008**: Toda alteração de registro feita pelos scripts MUST passar pelo `wrahost` (constituição II). Os scripts não gravam no registro diretamente.
- **FR-009**: O instalador do cliente MUST funcionar sem administrador, instalar na pasta do usuário e criar um atalho no Menu Iniciar. O desinstalador remove programa e atalho, e os dados do usuário só com opção explícita.
- **FR-010**: Instaladores e desinstaladores MUST ter um modo de simulação que mostra o que seria feito sem alterar nada.

### Key Entities

- **Host Package**: `wrahost.exe`, `WRApperture.Agent.exe`, scripts de instalação e desinstalação, README curto.
- **Client Package**: `WRApperture.exe`, scripts de instalação e desinstalação.
- **Agent Service**: serviço do Windows `WRAppertureAgent`, conta LocalSystem, início automático, reinício em falha.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um administrador deixa um PC Windows Pro limpo pronto para receber clientes em menos de 5 minutos, com um único comando.
- **SC-002**: Após reiniciar o host, o agente responde em até 1 minuto depois do boot, sem login.
- **SC-003**: Reinstalar (atualizar) não exige reparear nenhum cliente: 0 tokens invalidados.
- **SC-004**: O cliente roda num PC sem .NET instalado.
- **SC-005**: Depois da desinstalação com limpeza completa, o diagnóstico do host volta ao estado anterior à instalação.

## Assumptions

- Arquitetura x64. ARM64 fica fora do escopo da v1.
- Executáveis não assinados na v1. SmartScreen pode alertar, e isso é documentado.
- Os instaladores são scripts PowerShell (Windows PowerShell 5.1, presente em todo Windows 10/11). MSI ou MSIX ficam para depois.
- Pacotes distribuídos manualmente (zip). Atualização automática fica fora do escopo.
