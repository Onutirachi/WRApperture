# Feature Specification: Atualização em tempo real

**Feature Branch**: `20261016-142601-live-sync`

**Created**: 2026-10-16

**Status**: Draft

**Input**:

- "A atualização automática dos itens no cliente não é em tempo real, ou demora. Tenho que usar o F5 consideravelmente."
- "Existe solução melhor pra tudo atualizar em tempo real? Não sei se isso consome muito recurso ou é inviável."

## Como era

O cliente sincronizava só o host selecionado, nestes momentos:

- ao abrir;
- ao trocar de host;
- a cada 5 minutos;
- ao voltar para a janela depois de 1 minuto;
- no F5.

Um app publicado no host podia levar até 5 minutos para aparecer. Os cartões dos outros hosts só se atualizavam quando eram selecionados.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mudanças no host aparecem sozinhas (Priority: P1)

Algo muda no host, por exemplo:

- um app é publicado, editado ou removido;
- uma VPN conecta ou cai;
- um problema de acesso aparece ou é resolvido;
- o host é atualizado.

O cliente mostra a mudança em poucos segundos, sem F5, para **todos** os hosts da lista, não só o selecionado.

### User Story 2 - Host desligado e religado (Priority: P2)

- Quando o host para de responder, o cartão dele ganha a etiqueta **Offline**.
- Quando ele volta, a etiqueta some e a lista se atualiza.

## Requisitos e decisões

- **Long polling:**
  - O cliente pergunta ao agente: "mudou algo desde a versão X?".
  - O agente segura a pergunta até algo mudar, por no máximo 50 segundos, e responde com a versão atual.
  - Se a versão mudou, o cliente sincroniza aquele host, do jeito que já fazia: lista de apps, ícones e informações do host.
  - Funciona pelo mesmo HTTPS, com a mesma porta, o mesmo token e o mesmo certificado fixado. Não abre porta nem regra de firewall nova.
- **Versão:** um hash do que o cliente mostra:
  - os apps visíveis (alias, nome, caminho, ícone);
  - a porta RDP e o nome do host;
  - os problemas de acesso e o usuário sugerido;
  - as VPNs conectadas;
  - a versão do agente.
- **Custo:**
  - O agente recalcula a versão no máximo a cada 2 segundos: lê algumas chaves do registro e as redes conectadas.
  - O resultado é compartilhado por todos os clientes que estão esperando.
  - Os problemas de acesso continuam em cache por 1 minuto, porque a checagem deles é mais cara.
  - No cliente fica uma requisição parada por host. Ela custa praticamente nada até o agente responder.
- **Contrato:**
  - `GET /api/changes?since=<versão>` → `ChangesDto(Version)`;
  - recurso `changes` em `AgentFeatures`.
  - Agentes anteriores à 0.20.0 não têm essa rota. Para eles continua valendo a sincronização de antes: a cada 5 minutos, ao voltar para a janela e no F5.
- **Falhas:**
  - Se o host não responde, o cartão fica Offline e o cliente tenta de novo a cada 30 segundos.
  - Token recusado ou certificado trocado dispara a sincronização normal. Ela já trata esses casos: tira o host removido da lista, por exemplo.
