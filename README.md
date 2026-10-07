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
| `WRApperture.Host.Manager` | ⏳ | Interface gráfica do host |
| `WRApperture.Client` | ✅ | App WPF: pareia com o host, mostra os apps em grade e abre cada um em sua janela com um clique |
| `WRApperture.Client.Core` | ✅ | Lógica do cliente: pareamento com pinning, cache offline, geração de `.rdp`, credenciais e atalhos |

## Instalação (pacotes de release)

```powershell
.\scripts\build-release.ps1        # roda os testes e gera dist\WRApperture-Host-<v>.zip e dist\WRApperture-Client-<v>.zip
```

Os executáveis são autônomos e não precisam de .NET instalado.

**Host** (PC cujos apps serão usados remotamente):
1. Extraia `WRApperture-Host-<v>.zip` e dê duplo clique em `Instalar.cmd`, aceitando o UAC.
2. O instalador:
   - prepara o host;
   - registra o agente como serviço automático, com reinício em caso de falha;
   - libera a porta no firewall (redes de domínio e privadas);
   - mostra o endereço, a porta, o **token** e o fingerprint.
3. Publique apps com `wrahost publish ...`. O `wrahost` fica no PATH.

Rodar o instalador de novo atualiza a versão sem invalidar o token. Para desinstalar, dê duplo clique em `Desinstalar.cmd`, no pacote ou em `C:\Program Files\WRApperture`. Ele pede administrador e pergunta se deve desfazer tudo, inclusive o token.

Para usar no dia a dia não é preciso abrir o PowerShell: tudo tem um `.cmd` de duplo clique (`Instalar.cmd` e `Desinstalar.cmd`), que libera a execução de scripts só para aquela chamada.

**Cliente** (outro computador): extraia `WRApperture-Client-<v>.zip` e dê duplo clique em `Instalar.cmd`. Não precisa de administrador. Os atalhos "WRApperture" e "Desinstalar WRApperture" aparecem no Menu Iniciar. O desinstalador pergunta se deve apagar também os PCs pareados e as senhas salvas.

Os executáveis não são assinados, então o SmartScreen pode exibir um aviso na primeira execução.

## Desenvolvimento: uso do `wrahost` a partir do código (terminal **como administrador**)

```powershell
dotnet build
$wrahost = "src\WRApperture.Host.Cli\bin\Debug\net8.0-windows10.0.17763.0\wrahost.exe"

& $wrahost check                      # diagnóstico do host
& $wrahost prepare                    # liga RDP, NLA, allow-list e firewall (reversível com 'revert')
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

Na primeira execução, o agente cria um certificado autoassinado em `LocalMachine\My`. A API está descrita em [contracts/http-api.md](specs/20261007-105635-host-agent/contracts/http-api.md). A instalação como serviço do Windows fica para a feature de empacotamento.

## Cliente (no outro computador)

```powershell
dotnet run --project src/WRApperture.Client
```

1. Clique em **Adicionar host** e informe o IP ou nome do PC remoto, a porta `5080` e o token.
2. Confira se o fingerprint exibido é igual ao de `wrahost agent show` no host e confirme.
3. Clique em um app: só a janela dele abre. Vários apps compartilham a mesma sessão.
4. Opcional: no menu **⋯**, use **Salvar credenciais do host…** para abrir sem digitar a senha. Com o botão direito em um app, use **Criar atalho na área de trabalho**.

**Abrir sem alerta de segurança.** Desde a atualização de abril de 2026, o Windows mostra um alerta a cada abertura de um arquivo `.rdp` não assinado.
- No primeiro clique em um app, o cliente oferece **"Abrir apps sem alerta"**. Também está no menu **⋯**.
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
