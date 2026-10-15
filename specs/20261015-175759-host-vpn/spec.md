# Feature Specification: VPN do host

**Feature Branch**: `20261015-175759-host-vpn`

**Created**: 2026-10-15

**Status**: Draft

**Input**:

- "A mensagem no rodapé do cliente mostra que a rede é pública no host quando o túnel está ativo, mas mesmo assim funciona."
- "Se tiver como mostrar que o host tem alguma VPN ativa, seria uma boa."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - VPN não é "rede Pública" (Priority: P1)

O adaptador da VPN (no FortiClient, "Fortinet SSL VPN Virtual Ethernet Adapter") vira uma rede à parte no Windows, classificada como Pública. Os outros PCs nunca entram por ela.

- O item "Rede aceita conexões de outros PCs" passa a olhar só as redes locais.
- O botão Resolver nunca muda a categoria da rede da VPN.

### User Story 2 - Ver a VPN do host (Priority: P2)

- **No app do host:** "Conexão com os clientes" ganha a linha **VPN**, com a VPN conectada ou "nenhuma conectada".
- **No cliente:** ao lado do seletor de host aparece "VPN ativa: Fortinet SSL VPN" enquanto o host estiver conectado a uma VPN. A informação se atualiza a cada sincronização.

## Requisitos e decisões

- **Como uma rede é reconhecida como VPN:** pelo adaptador por trás dela (`INetwork.GetNetworkConnections` → `GetAdapterId`). Contam como VPN:
  - adaptadores PPP (as VPNs do próprio Windows), que aparecem pelo nome da conexão;
  - adaptadores com "VPN", "WireGuard" ou "TAP-Windows" na descrição.
- **O tipo "Tunnel" não basta:** o Teredo, o 6to4 e o IP-HTTPS do Windows também têm esse tipo.
- **Contrato:** `HostInfoDto.Vpns`, lido a cada requisição, porque a VPN sobe e cai a qualquer momento. Agentes anteriores à 0.20.0 não enviam nada, e o cliente não mostra nada.
