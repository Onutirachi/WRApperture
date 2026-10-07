# Backlog

Ideias para futuras features. Cada item vira uma spec (`/speckit-specify`) quando for priorizado.

| # | Ideia | Notas |
|---|---|---|
| 1 | Abrir apps com **duplo clique**, como no Windows | Hoje é clique simples. Seleção com um clique, abertura com dois, e Enter abre o app selecionado |
| 2 | Opção de abrir a **área de trabalho completa** do host, como padrão ou atalho | A definir: um tile "Área de trabalho" na grade ou uma preferência por host? Usa o mesmo pareamento e as mesmas credenciais |
| 3 | **Tema escuro** | Seguir o tema do Windows, com opção manual. Hoje as cores do sistema são fixadas no `App.xaml` |
| 4 | **Atalho na área de trabalho** para os apps | Já existe: botão direito no app → "Criar atalho na área de trabalho". Avaliar deixar mais visível e adicionar ao Menu Iniciar |
| 5 | Minimizar o painel para a **bandeja do Windows** | Ícone na bandeja com o menu dos apps e opção de iniciar com o Windows |
| 6 | Interface **bilíngue** (português e inglês) | Arquivos de recursos `.resx` no cliente, no `wrahost` e no instalador |
| 7 | **Renomear apps no cliente** (apenas visual) | Nome local por host e app, guardado no `hosts.json`, sem alterar o host |

## Limitações conhecidas

- Ao logar no host com apps remotos abertos, o cliente mostra o aviso de desconexão do `mstsc`. Ele vem do cliente RDP do Windows e não pode ser suprimido sem substituir o `mstsc` por um cliente RDP próprio (controle ActiveX).
