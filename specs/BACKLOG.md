# Backlog

Ideias para futuras features. Cada item vira uma spec (`/speckit-specify`) quando for priorizado.

| # | Ideia | Notas |
|---|---|---|
| 5 | Minimizar o painel para a **bandeja do Windows** | Ícone na bandeja com o menu dos apps e opção de iniciar com o Windows |

Feitos: 1 (duplo clique) e 7 (renomear apps no cliente) em `20261007-145218-client-tiles`; 2 (área de trabalho completa) em `20261007-150633-full-desktop`; 3 (tema escuro) em `20261007-152301-dark-theme`; 6 (idiomas) em `20261007-152909-localization`; 4 (atalhos no Menu Iniciar) em `20261007-154623-start-menu-shortcuts`.

## Limitações conhecidas

- Ao logar no host com apps remotos abertos, o cliente mostra o aviso de desconexão do `mstsc`. Ele vem do cliente RDP do Windows e não pode ser suprimido sem substituir o `mstsc` por um cliente RDP próprio (controle ActiveX).
