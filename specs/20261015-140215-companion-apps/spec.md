# Feature Specification: Iniciar junto

**Feature Branch**: `20261015-140215-companion-apps`

**Created**: 2026-10-15

**Status**: Draft

**Input**:

- "Coloquei o FortiClient para ser aberto remoto. Ele abre normal, mas a janelinha de login não vem junto, só a interface principal. Às vezes funciona, às vezes não."
- Diagnóstico: a janela de login (`FortiAuth.exe`) é aberta pelo `FortiTray.exe`, que o Windows inicia com o Explorer. Numa sessão de app remoto não há Explorer, então ele não roda. Iniciá-lo à mão na sessão abriu a janela na hora.
- "O usuário tem que definir manualmente qual app iniciar junto?" Não: detectar pela pasta do app.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Funciona sem configurar (Priority: P1)

Ao abrir o FortiClient pelo cliente, a janela de login aparece como no PC local. Ninguém precisa saber que ela depende do `FortiTray.exe`.

- Quando um app publicado roda numa sessão de app remoto, o agente inicia nessa sessão os **programas de inicialização da pasta do app** que ainda não estão rodando.
- Programas de inicialização são as chaves Run da máquina (64 e 32 bits) e do usuário e a pasta Inicializar de todos os usuários. Os desativados no Gerenciador de Tarefas não entram.
- Um programa pertence ao app quando fica na mesma pasta, ou quando uma das duas pastas contém a outra.
  - A pasta que contém a outra precisa estar pelo menos dois níveis abaixo da unidade: `C:\Program Files` não conta, `C:\Program Files\Fortinet` conta.
  - Nada da pasta do Windows entra. Por exemplo, o Bloco de Notas não puxa o ícone de segurança do Windows.
- Também entram os **programas de bandeja da pasta do app**: arquivos `.exe` com "Tray" como palavra do nome, como `FortiTray.exe` e `lghub_system_tray.exe`, mas não `Strayer.exe`.
  - Alguns fabricantes iniciam esses programas por um serviço, não pela inicialização do Windows. No FortiClient, quem inicia o `FortiTray.exe` é o `scheduler.exe` ("FortiClient Service Scheduler"). Por isso a primeira versão da regra não o encontrava.
- **Programas sem argumentos são abertos pelo Explorer da sessão** (`explorer.exe "programa"`), como um clique duplo.
  - Testes no Mongeral: o `FortiTray` aberto pelo agente ou pelo PowerShell mostra o login, mas a VPN não sobe ("SSL VPN connection is down"). Aberto pelo Explorer, o túnel sobe.
  - O agente encerra esse Explorer assim que o programa aparece, ou após 15 segundos, porque uma sessão com Explorer conta como área de trabalho e a próxima abertura de app a encerraria.
  - Programas com argumentos (o Explorer não os repassa) continuam abertos direto.
- Cada programa é iniciado uma vez por sessão. Se ele fechar, não é reaberto até a próxima sessão.
- Sessões de área de trabalho completa ficam de fora: lá o próprio Explorer inicia esses programas.

### User Story 2 - Ver e ajustar no app do host (Priority: P2)

- O editor do app ganha a seção **Iniciar junto**. Ela mostra os programas do usuário da tela do PC ou, se ninguém estiver nela (app do host usado por Área de Trabalho Remota), os do próprio usuário do app:
  - a caixa "Iniciar os programas de inicialização deste app (da pasta dele)", ligada por padrão, com o que foi encontrado ("Encontrados: FortiClient (FortiTray.exe)");
  - a lista **Iniciar também**, para escolher outros `.exe`.
- A lista de apps mostra "inicia com FortiClient".
- No `wrahost publish`, as opções `--start-with <exe>` (pode repetir) e `--no-auto-start-with`.

## Requisitos e decisões

- **Onde fica guardado:** junto do app em `TSAppAllowList\Applications\<alias>`, gravado só pelo Core.
  - `WRAppertureStartWith` (REG_MULTI_SZ): os programas escolhidos.
  - `WRAppertureAutoStartWith` (DWORD 0): automático desligado.
  - Os dois só são gravados quando diferem do padrão, então apps antigos e apps sem nada configurado ficam com o registro como antes.
- **Como o agente inicia:** ele verifica as sessões a cada 3 segundos. Os apps publicados e os programas encontrados ficam em cache por 30 segundos. O agente inicia os programas como o usuário da sessão, na tela dele (`CreateProcessAsUser`), do mesmo jeito que já iniciava o Explorer.
- **Alternativas descartadas:**
  - Configuração só manual: exigiria entender o problema.
  - Iniciar todos os programas de inicialização em toda sessão: OneDrive, Teams e afins abririam em cada sessão remota e apareceriam no cliente.
