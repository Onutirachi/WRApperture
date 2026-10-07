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
| `WRApperture.Client` | ⏳ | Lista os apps do host e abre com um clique |

## Uso rápido do `wrahost` (no host, terminal **como administrador**)

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

Para abrir o app no cliente enquanto o app cliente não fica pronto, crie um `.rdp` com o conteúdo abaixo:

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
```

Para validar de ponta a ponta, siga [specs/20261006-182347-host-core/quickstart.md](specs/20261006-182347-host-core/quickstart.md).
