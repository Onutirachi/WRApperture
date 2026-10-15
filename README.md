# WRApperture

Abra apps individuais de um PC Windows (host) a partir de outro computador (cliente), cada um na sua própria janela, sem abrir a área de trabalho remota inteira. Usa o **RemoteApp** nativo do RDP, sem servidor RDS. Também funciona em Windows 10/11 **Pro, Enterprise e Education**.

> No Windows Pro só existe **uma sessão por vez**. Ao abrir um RemoteApp, quem estiver usando o host localmente é desconectado. Vários apps abertos pelo mesmo cliente e usuário compartilham essa sessão.

## Componentes

| Componente | Status | Descrição |
|---|---|---|
| `WRApperture.Host.Core` | ✅ | Publica apps (registro `TSAppAllowList`), prepara o host (RDP, NLA, firewall), descobre apps instalados e extrai ícones |
| `wrahost` (CLI) | ✅ | Linha de comando sobre o Core, para uso manual e automação |
| `WRApperture.Host.Agent` | ✅ | Serviço HTTPS (porta 5080) que expõe apps, ícones e dados de conexão ao cliente, protegido por token de pareamento |
| `WRApperture.Shared` | ✅ | Contrato da API (rotas, DTOs, fingerprint de certificado), compartilhado entre o Agent e o Client |
| `WRApperture.Host.Manager` | ✅ | Interface gráfica do host (`WRApperture.Host.exe`): arrastar atalhos/programas para publicar, editar, remover, preparar o PC, gerar token |
| `WRApperture.Ui` | ✅ | Tema visual compartilhado entre o cliente e o Manager |
| `WRApperture.Client` | ✅ | App WPF: pareia com o host, mostra os apps em grade e abre cada um em sua janela com duplo clique |
| `WRApperture.Client.Core` | ✅ | Lógica do cliente: pareamento com pinning, cache offline, geração de `.rdp`, credenciais e atalhos |

## Instalação

Baixe os instaladores da página de [Releases](https://github.com/Onutirachi/WRApperture/releases/latest). Os executáveis são autônomos e não precisam de .NET instalado. Eles não têm assinatura de código, então o SmartScreen pode avisar na primeira execução: use **Mais informações → Executar assim mesmo**.

**Host** (o PC cujos apps serão usados remotamente): `WRApperture-Host-Setup-<versão>.exe`.
1. Rode o instalador e aceite o pedido de administrador. Deixe marcado **Criar um atalho na área de trabalho**, se quiser o atalho.
2. O instalador:
   - prepara o PC para RemoteApp;
   - registra o agente como serviço automático, com reinício em caso de falha;
   - libera no firewall a porta 5080 (TCP) e a 5081 (UDP, para os clientes acharem o PC), nas redes de domínio e privadas.
3. Ao concluir, abra o **WRApperture (Host)**. Em **Este PC**, ele mostra o que ainda impede o acesso remoto e oferece um botão para cada item:
   - Área de Trabalho Remota desligada, inclusive por política ou por serviço desativado;
   - firewall bloqueando a Área de Trabalho Remota;
   - rede marcada como **Pública**;
   - conta do Windows sem senha (**Definir senha…**);
   - usuário padrão fora do grupo de Área de Trabalho Remota;
   - conta Microsoft com "só Windows Hello";
   - PC que entra em suspensão.

   Antes de mudar qualquer coisa, **Resolver** explica o que muda, por quê e o efeito colateral. Tudo pode ser desfeito com `wrahost revert`, menos a senha.
4. Arraste para a janela os atalhos (da área de trabalho ou do Menu Iniciar) ou programas `.exe` que quer usar remotamente. Nome, caminho, argumentos e ícone são preenchidos automaticamente. O botão **Do Menu Iniciar…** publica vários apps instalados de uma vez.
5. Alguns apps dependem de um programa que o Windows inicia com o login. Exemplo: a janela de login do FortiClient vem do `FortiTray.exe`. Numa sessão de app remoto esses programas não rodam, então o agente inicia junto os programas de inicialização que ficam na pasta do app. O editor do app mostra o que foi encontrado em **Iniciar junto**, onde também dá para desligar isso ou acrescentar outro programa.

O Windows Home não suporta RemoteApp; o instalador avisa e não instala.

**Cliente** (o outro computador): `WRApperture-Client-Setup-<versão>.exe`. Não precisa de administrador.

**Desinstalar:** em Configurações → Aplicativos → Aplicativos instalados.
- O host pergunta se deve desfazer também a preparação do PC e apagar o token.
- O cliente pergunta se deve apagar os PCs pareados e as senhas salvas.

**Atualizações:** o cliente e o host verificam as releases deste repositório e, com **Atualizar automaticamente** ligado (padrão), se atualizam sozinhos. O agente do host faz isso mesmo sem ninguém usando o PC.
- O instalador baixado só é executado se o SHA-256 conferir com o `SHA256SUMS.txt` da release.
- O cliente avisa quando um host está numa versão antiga e pode pedir que ele se atualize.

Instalações anteriores feitas com `Instalar.cmd` são atualizadas no lugar pelos novos instaladores. O token, os apps publicados e os PCs pareados são mantidos.

## Gerar uma release

```powershell
.\scripts\build-release.ps1        # testes + dist\WRApperture-*-Setup-<v>.exe + dist\SHA256SUMS.txt (requer Inno Setup 6)
```

No GitHub:
1. Atualize `<Version>` no `Directory.Build.props`.
2. Faça commit e envie uma tag `v<versão>`, por exemplo `git tag v0.15.0 && git push origin v0.15.0`.
3. A Action **Release** compila, roda os testes .NET e Pester, gera os instaladores e publica a release. Os apps instalados passam a se atualizar para ela.

Os scripts PowerShell em `packaging/` continuam funcionando para instalação manual e automação: `Instalar.cmd`, `-DryRun`, `-NoDesktopShortcut` e os demais parâmetros.

## Desenvolvimento: uso do `wrahost` a partir do código (terminal **como administrador**)

```powershell
dotnet build
$wrahost = "src\WRApperture.Host.Cli\bin\Debug\net8.0-windows10.0.17763.0\wrahost.exe"

& $wrahost check                      # diagnóstico do host
& $wrahost prepare                    # liga RDP, NLA, allow-list e firewall (reversível com 'revert')
& $wrahost fix NetworkProfile         # resolve um item de 'check --json' que o prepare não muda (rede, grupo, Windows Hello)
& $wrahost discover                   # apps instalados
& $wrahost publish --path "%SystemRoot%\System32\notepad.exe" --name "Bloco de Notas"
& $wrahost list
& $wrahost unpublish bloco-de-notas
```

## Agente (no host, terminal **como administrador**)

```powershell
& $wrahost agent token     # gera o token de pareamento (exibido uma vez; regenerar revoga o anterior)
& $wrahost agent show      # porta, pareamento e fingerprint do certificado
netsh advfirewall firewall add rule name="WRApperture Agent" dir=in action=allow protocol=TCP localport=5080
dotnet run --project src/WRApperture.Host.Agent
```

Na primeira execução, o agente cria um certificado autoassinado em `LocalMachine\My`. Para o pareamento por aprovação, o agente abre `WRApperture.Host.exe --pairing` na tela de quem usa o PC. Rodando a partir do código, aponte para ele com `$env:Pairing__ApproverPath = "<repo>\src\WRApperture.Host.Manager\bin\Debug\net8.0-windows10.0.17763.0\WRApperture.Host.exe"`. A API está descrita em [contracts/http-api.md](specs/20261007-105635-host-agent/contracts/http-api.md). A instalação como serviço do Windows fica para a feature de empacotamento.

## Cliente (no outro computador)

```powershell
dotnet run --project src/WRApperture.Client
```

1. Clique em **Adicionar host**. Os PCs da rede com o WRApperture (Host) aparecem pelo nome. Escolha um.
2. No PC remoto, quem estiver usando vê uma janela com o nome deste computador e um código. Confira se é o mesmo código mostrado no cliente e clique em **Permitir**.
   - Cada computador recebe um token próprio, que pode ser revogado em **Dispositivos pareados** no app do host.
   - Logo depois, o cliente pede o usuário e a senha do Windows daquele PC, já sugerindo o usuário certo (o e-mail, no caso de conta Microsoft).
   - Por VPN ou outra rede, use **Adicionar manualmente** com o endereço, a porta `5080` e o token (**Token manual…** no app do host). Nesse caso, confira o fingerprint exibido no app do host.
3. Dê duplo clique em um app (ou selecione e tecle Enter): só a janela dele abre. Vários apps compartilham a mesma sessão.
4. O menu **⋯** reúne o que é do host selecionado: a versão dele, **Salvar credenciais do host…**, **Sincronizar agora** (F5), **Atualizar o PC remoto** e **Remover host**. A lista de apps se atualiza sozinha a cada 5 minutos e ao voltar para a janela. As configurações deste computador ficam no botão **⚙**: bandeja, iniciar com o Windows, tema, idioma, atualizações e a versão do cliente.
   Opcional: no menu **⋯**, use **Salvar credenciais do host…** para abrir sem digitar a senha. Com o botão direito em um app, use **Criar atalho na área de trabalho**, **Adicionar ao Menu Iniciar** (o app aparece na busca do Windows e pode ser fixado) ou **Renomear…** (F2). O novo nome vale só neste computador; o PC remoto não muda.
5. O primeiro bloco da grade, **Área de trabalho**, abre o PC remoto inteiro em tela cheia, com as mesmas credenciais e sem o alerta de segurança.
   - Se você já estiver logado lá, a sessão é reaproveitada com os programas abertos.
   - Apps remotos abertos são fechados antes, porque o Windows não mistura os dois modos.
   - Outro usuário logado é desconectado e os programas dele continuam abertos.
   - Essa preparação exige o host 0.6.0 ou mais recente. Com um host mais antigo, o Windows pergunta como numa conexão remota comum.
6. O visual segue o modo claro/escuro do Windows. Para fixar um tema, use o botão **⚙** (cliente) ou o botão **◐** (app do host).
7. O idioma segue o idioma de exibição do Windows (hoje português e inglês; os demais usam inglês). Para escolher outro, use **Idioma (Language)…** no botão **⚙** do cliente ou no botão **◐** do app do host. Para acrescentar um idioma, veja [localization/README.md](localization/README.md).
8. O cliente fica na bandeja do Windows (perto do relógio). O botão direito no ícone abre qualquer app direto (com vários hosts, um submenu por host), e fechar a janela mantém o cliente ali. Use **Sair** para encerrar. No botão **⚙**, escolha se fechar mantém o cliente na bandeja e se ele deve **Iniciar com o Windows**.
9. Os atalhos criados pelo cliente se chamam "<app> - <host>" (o mesmo app em dois hosts gera dois atalhos distintos) e levam um selo azul do WRApperture no canto, para diferenciá-los dos apps instalados no próprio computador. Os desenhos dos ícones ficam em `assets/icons/*.svg`; depois de mudar algum, rode `scripts/build-icons.ps1`.

**Abrir sem alerta de segurança.** Desde a atualização de abril de 2026, o Windows mostra um alerta a cada abertura de um arquivo `.rdp` não assinado.
- Ao abrir um app pela primeira vez, o cliente oferece **"Abrir apps sem alerta"**. Também está no botão **⚙**.
- Ele pede administrador **uma única vez** e configura este PC para assinar as conexões que o próprio WRApperture cria:
  - um certificado de máquina, com a chave liberada só para o seu usuário;
  - uma cópia no Raiz, restrita a "Autenticação de Área de Trabalho Remota", para não servir para assinar programas;
  - a política de publicadores confiáveis do RDP.
- Arquivos `.rdp` de outras origens continuam exibindo o alerta.
- A configuração é removida pelo desinstalador com `-RemoveUserData`.

Na primeira conexão, o Windows pode avisar sobre o certificado RDP do host. Marque "Não perguntar novamente para conexões com este computador".

**Alternar entre usar o PC remoto localmente e pelo cliente.** No Windows Pro, um usuário não pode ter ao mesmo tempo a sessão local e os apps remotos.
- **Abrir um app com você logado no PC remoto** (na tela ou por Área de Trabalho Remota comum): o cliente encerra a sua sessão lá automaticamente, sem perguntar em nenhuma das pontas, e abre o app. **Os programas abertos nessa sessão são fechados sem salvar.** Para ser perguntado antes, use **⋯ → Perguntar antes de encerrar a sessão do PC remoto**. Sessões ativas de **outros** usuários são apenas desconectadas (os programas deles continuam abertos), nunca encerradas. Assim, o Windows não pede confirmação em nenhuma das pontas.
- **Logar no PC remoto com apps abertos no cliente:** o Windows desconecta o cliente, e o agente inicia a área de trabalho nessa sessão. Você continua com os mesmos programas abertos no PC, sem perder trabalho.
- Para diagnóstico, rode `wrahost sessions` no host. Ele lista as sessões e o tipo de cada uma.

**Barra de idioma flutuante.** O `wrahost prepare` (rodado pelo instalador do host) oculta a barra de idioma nos perfis carregados. Para outro usuário do host, ele mesmo roda `wrahost langbar hide`, sem precisar de administrador. A mudança vale a partir de uma **nova sessão**: faça logoff da sessão remota antes de testar.
Os dados do cliente ficam em `%LOCALAPPDATA%\WRApperture\client`. O token fica protegido com DPAPI e a senha fica no Gerenciador de Credenciais do Windows.

### Sem o cliente: `.rdp` manual

Para abrir um app sem o cliente, crie um `.rdp` com o conteúdo abaixo:

```
full address:s:<IP-OU-NOME-DO-HOST>
remoteapplicationmode:i:1
remoteapplicationprogram:s:||bloco-de-notas
remoteapplicationname:s:Bloco de Notas
disableremoteappcapscheck:i:1
```

Códigos de saída: `0` ok, `1` validação, `2` requer administrador, `3` edição não suportada.
Os logs ficam em `%ProgramData%\WRApperture\logs`.

## Desenvolvimento (SDD)

O projeto segue Spec-Driven Development com o [GitHub Spec Kit](https://github.com/github/spec-kit):

- A constituição fica em [.specify/memory/constitution.md](.specify/memory/constitution.md).
- As features ficam em `specs/<YYYYMMDD-HHMMSS>-<slug>/` (spec, plan, tasks). O ID é um timestamp, para evitar conflito entre pessoas.
- O fluxo é `/speckit-specify` → `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`.

```powershell
dotnet build      # sem warnings (TreatWarningsAsErrors)
dotnet test       # unit + integração (ícones, só leitura)
dotnet test --filter "Category!=Integration"   # apenas unitários
Invoke-Pester tests\packaging                  # scripts de instalação (Windows PowerShell 5.1)
```

Para validar de ponta a ponta, siga [specs/20261006-182347-host-core/quickstart.md](specs/20261006-182347-host-core/quickstart.md).

## Licença

[MIT](LICENSE) © Carlos Landini
