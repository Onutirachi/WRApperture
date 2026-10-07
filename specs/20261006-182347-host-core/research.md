# Research: Host Core

## R1. Onde e como o Windows Pro guarda os RemoteApps

- **Decision**: Usar `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Terminal Server\TSAppAllowList`.
  - O valor `fDisabledAllowList` (DWORD) fica em `0`, para respeitar a lista. Com `1`, qualquer programa poderia ser iniciado, o que é um risco.
  - Cada app é uma subchave `Applications\<Alias>` com estes valores:

  | Valor | Tipo | Uso |
  |---|---|---|
  | `Name` | REG_SZ | Nome de exibição |
  | `Path` | REG_SZ | Caminho expandido do exe |
  | `VPath` | REG_SZ | Caminho como digitado (pode conter `%VAR%`) |
  | `IconPath` | REG_SZ | Arquivo do ícone (default = `Path`) |
  | `IconIndex` | DWORD | Índice do ícone |
  | `CommandLineSetting` | DWORD | 0 = sem argumentos, 1 = qualquer um, 2 = só `RequiredCommandLine` |
  | `RequiredCommandLine` | REG_SZ | Argumentos fixos |
  | `ShowInTSWA` | DWORD | 1 = visível em listagens |
  | `SecurityDescriptor` | REG_SZ | Vazio = todos os usuários com direito de RDP |

  Um cliente `mstsc` com `remoteapplicationmode:i:1` e `remoteapplicationprogram:s:||<Alias>` abre o app pelo alias.
- **Rationale**: É o mesmo mecanismo que o RDS usa e que ferramentas como "RemoteApp Tool" usam no Windows cliente. Dispensa serviços adicionais.
- **Alternatives considered**: `fDisabledAllowList=1` com `alternate shell` no `.rdp`. Foi rejeitado porque permitiria abrir qualquer executável (constituição IV).

## R2. Preparação do host (diagnóstico e correção)

| Check | Onde | Esperado |
|---|---|---|
| Edição suportada | `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion` → `EditionID` | `Professional*`, `Enterprise*`, `Education*`, `Server*`. `Core*` = Home → não suportado |
| RDP habilitado | `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server` → `fDenyTSConnections` | `0` |
| NLA | `...\Terminal Server\WinStations\RDP-Tcp` → `UserAuthentication` | `1` |
| Allow-list aplicada | `TSAppAllowList` → `fDisabledAllowList` | `0` (ou ausente) |
| Firewall RDP | Regras com `Grouping = "@FirewallAPI.dll,-28752"` | Todas habilitadas no perfil atual |

- **Decision (firewall)**: Usar o COM `HNetCfg.FwPolicy2` (via `Type.GetTypeFromProgID` + `dynamic`) e filtrar as regras pelo **grouping indireto** `@FirewallAPI.dll,-28752`.
- **Rationale**: O `netsh ... group="Remote Desktop"` depende do idioma: em PT-BR o grupo se chama "Área de Trabalho Remota". O grouping indireto é o mesmo em qualquer idioma.
- **Alternatives considered**: `netsh advfirewall` (depende do idioma) e o módulo PowerShell `NetSecurity` (exige runspace, pesado). Ambos rejeitados.
- **Decision (snapshot)**: Antes de alterar um item, gravar o valor anterior em `HKLM\SOFTWARE\WRApperture\PreparationSnapshot`, com um valor por check (o primeiro valor gravado nunca é sobrescrito). A reversão restaura e apaga o snapshot.
- **Rationale**: HKLM só é gravável por administrador, ao contrário de `%ProgramData%`. Usa a mesma abstração de registro (constituição II) e não cria banco de dados (constituição V).

## R3. Descoberta de apps instalados

- **Decision**:
  - Enumerar `*.lnk` recursivamente em `%ProgramData%\Microsoft\Windows\Start Menu\Programs` e `%AppData%\Microsoft\Windows\Start Menu\Programs`.
  - Resolver cada atalho com `IShellLinkW`/`IPersistFile` (COM interop próprio, CLSID `00021401-0000-0000-C000-000000000046`).
  - Para atalhos *advertised* de MSI, em que o alvo não é resolvível, usar `MsiGetShortcutTarget` + `MsiGetComponentPath` (P/Invoke em `msi.dll`).
  - Filtros:
    - o alvo existe e tem extensão `.exe`;
    - o nome do arquivo não casa com `unins*`, `uninstall*` ou `setup*`;
    - deduplicar por caminho do exe e argumentos, sem diferenciar maiúsculas de minúsculas.
- **Descoberto na implementação**: `MsiGetShortcutTarget` retorna `1603` em threads MTA, que são o padrão do .NET. Por isso a resolução roda em uma thread STA. Atalhos MSI não resolvidos apontam para stubs de ícone em `%SystemRoot%\Installer` e são descartados.
- **Rationale**: Reproduz o que aparece em "Todos os apps" (exceto UWP, fora de escopo) sem dependências externas.
- **Alternatives considered**:
  - `Shell.Application` via COM `dynamic`: mais lento e frágil.
  - Varrer `Uninstall` no registro: não fornece o exe principal.
  - `Windows.Management.Deployment` (só UWP).

## R4. Extração de ícones em PNG

- **Decision**: `PrivateExtractIconsW(path, index, size, size, ...)` em `user32.dll`, com `size` = 48 (configurável), seguido de `Icon.FromHandle` → `Bitmap` → `Save(Png)` e `DestroyIcon`. Fallback: `SHGetStockIconInfo(SIID_APPLICATION)`.
- **Rationale**: `Icon.ExtractAssociatedIcon` só devolve 32x32 e ignora o índice.
- **Alternatives considered**: `SHDefExtractIcon`, equivalente mas com uma API um pouco mais verbosa. Fica como alternativa se `PrivateExtractIcons` for descontinuada.

## R5. Alias

- **Decision**: Regex `^[A-Za-z0-9_-]{1,64}$`.
  - Geração automática: remover diacríticos (`NormalizationForm.FormD`), trocar caracteres inválidos por `-`, colapsar hífens repetidos e passar para minúsculas.
  - Em caso de colisão, adicionar sufixo `-2`, `-3`, ...
  - A comparação ignora maiúsculas e minúsculas, porque o registro também ignora.
- **Rationale**: É seguro para nome de chave de registro, para URL (`/api/apps/{alias}`) e para `||alias` no `.rdp`.

## R6. Pasta gravável por não-admin (aviso)

- **Decision**: Ler a ACL do diretório do exe com `DirectoryInfo.GetAccessControl()` (suportado no .NET 8 em Windows). Há risco se alguma ACE *Allow* com `Write`/`Modify`/`FullControl` pertencer a `Users (S-1-5-32-545)`, `Authenticated Users (S-1-5-11)`, `Everyone (S-1-1-0)` ou `INTERACTIVE (S-1-5-4)`. Também há risco se o caminho estiver sob `C:\Users\`.
- **Rationale**: Cobre os casos comuns (Downloads, AppData, pastas soltas em `C:\`) sem falso positivo em `Program Files` ou `Windows`.

## R7. Privilégios e logging

- **Decision**: `new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)` antes de qualquer escrita. Se falhar, lança `AdminRequiredException`.
- **Decision**: O Core depende só de `Microsoft.Extensions.Logging.Abstractions` (`ILogger<T>`). Os executáveis (CLI, Manager, Agent) plugam o Serilog.
- **Rationale**: Mantém o Core desacoplado. O Serilog continua sendo o provider oficial, como pede a constituição V.

## R8. CLI de validação

- **Decision**: Incluir nesta feature um CLI fino `wrahost`, que expõe as operações do Core: `list`, `publish`, `unpublish`, `check`, `prepare`, `revert`, `discover`, `icon`. A biblioteca usada é `System.CommandLine`.
- **Rationale**: Permite validar a feature de ponta a ponta antes de existir o Manager e atende ao pedido original de "algum script". Também serve para automação (ex.: publicar apps em vários PCs).
- **Alternatives considered**: Validar só por testes unitários. Foi rejeitado porque não prova que o `mstsc` abre o app.
