# Feature Specification: Instalação e pareamento facilitados

**Feature Branch**: `20261015-114136-easy-setup`

**Created**: 2026-10-15

**Status**: Draft

**Input**:

- "Hoje o usuário tem que copiar o token e o IP manualmente. Queria que o cliente encontrasse os hosts disponíveis na rede e fizesse o cadastro de forma automática, usando o nome do computador para identificação."
- "Para funcionar, o usuário tem que ter senha cadastrada e o acesso remoto permitido no host. Meu amigo teve dificuldade com rede pública, senha no usuário local e acionamento da área de trabalho remota."
- "Pode ser só permitir: fala quem está pedindo e se quer confirmar."
- "Seria bom ter um botão Resolver, com avisos do que vai mudar e por quê. No cliente, um popup ao incluir um host novo para já definir usuário e senha."
- Checks do painel "Este PC": manter só "Área de Trabalho Remota habilitada" e "Firewall libera a Área de Trabalho Remota"; acrescentar outros se necessário.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - O host diz o que falta e resolve (Priority: P1)

O painel "Este PC" do app do host mostra só o que importa para o acesso remoto funcionar, cada item com ✓ ou !:

| Item | Sempre visível | Botão |
|---|---|---|
| Área de Trabalho Remota habilitada | sim | Resolver |
| Firewall libera a Área de Trabalho Remota | sim | Resolver |
| Rede atual não é Pública | sim | Resolver (marca a rede como Privada) |
| A conta do Windows tem senha | sim (conta local) | Definir senha… |
| A política do Windows não bloqueia a Área de Trabalho Remota | só com pendência | Resolver (apaga o valor de política) |
| Serviço da Área de Trabalho Remota não está desativado | só com pendência | Resolver (tipo de inicialização Manual e inicia) |
| A conta pode entrar remotamente | só com pendência (usuário padrão fora do grupo) | Resolver (adiciona ao grupo pelo SID S-1-5-32-555) |
| Conta Microsoft: entrar com senha | só para conta Microsoft | Resolver quando "só Windows Hello" está ligado |
| O PC não entra em suspensão | só com pendência (aviso) | Abrir configurações de energia |

- Antes de mudar qualquer coisa, **Resolver** explica o que vai mudar, por que e o efeito colateral, e pede confirmação.
- **Resolver tudo** aplica os itens automáticos visíveis e, em silêncio, os de segurança que deixaram de aparecer (NLA, lista de apps permitidos, barra de idioma).
- Edição do Windows só aparece quando não é suportada.
- A rede, o grupo de usuários e a senha nunca são alterados pelo instalador (`wrahost prepare`): só por um clique explícito no app do host (ou `wrahost fix <item>`).
- Tudo o que um Resolver muda pode ser desfeito pelo `wrahost revert`, exceto a senha.

### User Story 2 - O cliente já pede usuário e senha ao adicionar o host (Priority: P1)

- Logo após parear, abre "Como você entra no PC SALA?", com o usuário sugerido pelo host (`SALA\carlos`, ou o e-mail para conta Microsoft) e a dica certa ("senha da conta Microsoft, não o PIN"). Tem **Agora não**.
- Se o host relatar pendências, o cliente mostra um aviso: "O PC SALA ainda não está pronto: rede Pública. Resolva no app WRApperture (Host) do PC SALA."

### User Story 3 - Encontrar o host na rede e parear com um clique no host (Priority: P1)

1. No cliente, **Adicionar host** lista os PCs encontrados na rede pelo nome do computador.
2. Ao escolher um, o cliente mostra "Aguardando confirmação no PC SALA… Código 482 913".
3. No host, abre na tela de quem está usando o PC: "NOTEBOOK-JOAO (joao, 192.168.0.23) quer usar os apps deste PC. Código 482 913. Confira se é o mesmo na tela do outro computador. [Permitir] [Recusar]".
4. Permitido, o cliente recebe um token próprio e segue para o popup de credenciais (US2).

- Sem ninguém logado no host, o pedido é recusado na hora com essa explicação.
- O pedido expira em 2 minutos.
- "Adicionar manualmente" (endereço + token) continua disponível para VPN ou outra sub-rede.

### User Story 4 - Dispositivos pareados (Priority: P2)

O app do host lista os dispositivos pareados (nome, usuário, data) e permite remover cada um. O token manual continua funcionando (clientes antigos).

## Requisitos e decisões

- **Descoberta**: UDP na porta 5081 (`ApiContract.DiscoveryPort`).
  - O cliente envia por broadcast um pedido de pelo menos 256 bytes, para que a resposta nunca seja maior que o pedido e não sirva para amplificação.
  - O agente responde por unicast com nome, porta, fingerprint, versão e capacidades.
  - O firewall abre a porta só para redes de Domínio e Privada.
- **Pedido de pareamento**: `POST /pair/requests` sem token, fora de `/api`.
  - O cliente envia o nome e o usuário (limitados e higienizados no agente) e um nonce aleatório.
  - Código de verificação = 6 dígitos de `SHA-256("WRApperture-pairing" | fingerprint | nonce)`. O cliente calcula com o fingerprint que **ele** viu no TLS e o agente com o próprio, então um intermediário faz os códigos divergirem.
  - O cliente consulta `GET /pair/requests/{id}` até a decisão. O token é entregue uma única vez. Depois, o cliente fixa o fingerprint que viu no pedido.
  - Limites: um pedido pendente por IP, 5 pedidos por IP a cada 10 minutos e no máximo 3 pendentes no total.
- **Aprovação**: o agente (LocalSystem) abre `WRApperture.Host.exe --pairing <id>` na sessão de console ativa.
  - Essa janela lê os detalhes e envia a decisão por `localhost` (`/pair/approval/{id}`), autenticada por um segredo de uso único.
  - O segredo vai na linha de comando do processo, que só o próprio usuário e os administradores conseguem ler.
  - Aprovar não pede administrador: basta estar usando o PC.
- **Tokens por cliente**: guardados em `HKLM\SOFTWARE\WRApperture\Agent\Clients\<id>` (nome, usuário, data, salt e hash). O token único de antes continua válido.
- **Prontidão no cliente**: `/api/host` passa a trazer as pendências (códigos, que o cliente traduz) e o usuário sugerido. O recurso é anunciado por `AgentFeatures.Readiness`.
- **Conta sem senha**: detectada com `LogonUser` de senha vazia, só para contas locais. O resultado fica em cache por 1 hora por processo, porque uma tentativa errada conta para o bloqueio de conta.
- **Endereço do host**: o cliente guarda o nome do computador quando ele resolve para o IP que respondeu, e o IP caso contrário.

## Notas

- Nova dependência no Core: `System.ServiceProcess.ServiceController` (já usada pelo Manager), para consultar e iniciar o `TermService`.
- Nada muda para os clientes e hosts antigos: os recursos novos são anunciados em `AgentFeatures`.
