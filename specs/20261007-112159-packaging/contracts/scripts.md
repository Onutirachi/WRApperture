# Contract: Scripts de build e instalação

## Build (repo)
```
scripts/build-release.ps1 [-Version <x.y.z>] [-OutDir dist] [-SkipTests]
```
Saída: `dist/WRApperture-Host-<v>.zip` e `dist/WRApperture-Client-<v>.zip`. Se `-Version` for omitido, usa `<Version>` de `Directory.Build.props`.

## Pacote do host
| Arquivo | Uso |
|---|---|
| `Instalar.cmd` | Duplo clique: pede elevação e chama `Install-WRAppertureHost.ps1` |
| `Install-WRAppertureHost.ps1 [-InstallDir] [-AllowPublicNetwork] [-DryRun]` | Instala ou atualiza |
| `Uninstall-WRAppertureHost.ps1 [-InstallDir] [-Purge] [-DryRun]` | Remove (também é copiado para `InstallDir`) |
| `wrahost.exe`, `WRApperture.Agent.exe`, `appsettings.json` | Binários |
| `LEIA-ME.txt` | Passo a passo |

## Pacote do cliente
| Arquivo | Uso |
|---|---|
| `Instalar.cmd` | Duplo clique, sem elevação |
| `Install-WRAppertureClient.ps1 [-InstallDir] [-StartMenuDir] [-DryRun]` | Instala em `%LOCALAPPDATA%\Programs\WRApperture` e cria o atalho "WRApperture" no Menu Iniciar |
| `Uninstall-WRAppertureClient.ps1 [-InstallDir] [-StartMenuDir] [-RemoveUserData] [-DryRun]` | Remove. Com `-RemoveUserData`, apaga também hosts, ícones, logs e as credenciais `TERMSRV/*` dos hosts pareados |
| `WRApperture.exe` | Binário |

## Códigos de saída
`0` ok · `1` erro · `2` requer administrador · `3` edição do Windows não suportada.

## Novos comandos `wrahost`
```
wrahost agent show --json     → {"port":5080,"isPaired":true,"certificateFingerprint":"AB.."|null}
wrahost agent token --json    → {"token":"..."}
wrahost agent reset           (admin) apaga as configurações do agente e o certificado
```
