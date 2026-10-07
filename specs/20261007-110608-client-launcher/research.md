# Research: Client Launcher

## R1. Abrir só a janela do app
- **Decision**: Gerar um `.rdp` por app em `%LOCALAPPDATA%\WRApperture\client\rdp\<hostId>\<alias>.rdp` (UTF-16 LE com BOM, o formato que o próprio `mstsc` salva) e executar `mstsc.exe "<arquivo>"`. Chaves usadas:

  | Chave | Valor |
  |---|---|
  | `full address:s:` | `<endereço>` ou `<endereço>:<porta>` se a porta RDP ≠ 3389 |
  | `remoteapplicationmode:i:` | `1` |
  | `remoteapplicationprogram:s:` | `\|\|<alias>` |
  | `remoteapplicationname:s:` | nome de exibição |
  | `disableremoteappcapscheck:i:` | `1` (necessário em Windows cliente, que não anuncia a capacidade RAIL) |
  | `alternate shell:s:` | `rdpinit.exe` |
  | `username:s:` | usuário RDP salvo (se houver) |
  | `prompt for credentials:i:` | `0` |
  | `authentication level:i:` | `2` (avisa, mas permite, certificado RDP autoassinado) |
  | `autoreconnection enabled:i:` | `1` |
  | `redirectclipboard:i:` | `1` |

- **Session reuse (FR-009)**: o `mstsc` reaproveita a sessão RemoteApp quando `full address` e `username` são idênticos. O endereço vem sempre do `PairedHost.Address` digitado no pareamento, e não do `hostName` retornado pelo agente.
- **Alternatives**: Controle ActiveX `MsRdpClient` embutido no WPF: muito mais complexo e duplica o que o `mstsc` já faz. Rejeitado.

## R2. Credenciais RDP
- **Decision**: `CredWriteW` (advapi32) com `Type = CRED_TYPE_DOMAIN_PASSWORD (2)`, `TargetName = "TERMSRV/<endereço>"` e `Persist = CRED_PERSIST_LOCAL_MACHINE`. Remoção via `CredDeleteW`. É exatamente o que o `mstsc` lê.
- **Alternatives**: `cmdkey /generic:` cria uma credencial *generic*, que o `mstsc` não usa para NLA em todos os casos, e expõe a senha na linha de comando. Rejeitado.

## R3. Pareamento e pinning
- **Decision**: `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` → `CertificatePinValidator`:
  - **Pareamento**: aceita qualquer certificado e registra o fingerprint SHA-256 observado (`CertificateFingerprint.Compute`, de `WRApperture.Shared`). Em seguida compara com `HostInfoDto.CertificateFingerprint`. Se divergirem, recusa (FR-004).
  - **Uso normal**: aceita somente se o fingerprint observado for igual ao fixado. Caso contrário, lança `CertificateChangedException(observed)` e a UI oferece aceitar o novo fingerprint (FR-003).
  - Erros de nome e de cadeia são ignorados de propósito, porque o certificado é autoassinado e a confiança vem do pin.

## R4. Armazenamento local
- **Decision**: `%LOCALAPPDATA%\WRApperture\client\hosts.json`, gravado de forma atômica (arquivo temporário + `File.Move` com overwrite).
  - Token: `ProtectedData.Protect(UTF8(token), entropy "WRApperture.Client.v1", CurrentUser)` em Base64.
  - Ícones: `icons\<hostId>\<alias>.png` mais o ETag guardado no `hosts.json`.
- **Rationale**: Sem banco de dados (constituição V). DPAPI atende o princípio IV.

## R5. Atalho na área de trabalho
- **Decision**: Converter o PNG de 256 px em `.ico` (contêiner ICO com PNG embutido, suportado a partir do Vista), em `icons\<hostId>\<alias>.ico`. O `.lnk` é criado via COM `WScript.Shell` (`CreateShortcut`), com `TargetPath` = o `.rdp` do app e `IconLocation` = o `.ico`.
- **Alternatives**: Copiar o `.rdp` para a área de trabalho: o ícone seria o genérico do `mstsc`. Rejeitado.

## R6. Arquitetura
- **Decision**: Duas camadas:
  - `WRApperture.Client.Core` (classlib sem WPF, testável): `HostStore`, `AgentClient`, `CertificatePinValidator`, `RdpFileBuilder`, `RemoteAppLauncher` (atrás de `IProcessStarter`), `WindowsCredentialStore`, `IconStore`, `IcoWriter`, `DesktopShortcuts`.
  - `WRApperture.Client` (WPF + CommunityToolkit.Mvvm): só ViewModels e Views.
- **Rationale**: Testar a lógica sem subir WPF.

## R7. Logging
- **Decision**: Serilog em arquivo, em `%LOCALAPPDATA%\WRApperture\logs\client-.log`. Token e senha nunca são passados ao logger.

## R8. Descobertos na implementação
- **Tema do Windows**: o WPF herda `SystemColors` nos controles padrão. Em temas escuros ou de contraste, o texto ficava branco sobre os tiles brancos. Por isso o `App.xaml` fixa as chaves de `SystemColors` com a paleta clara do app.
- **Credenciais de teste**: `CredWriteW` com `CRED_TYPE_DOMAIN_PASSWORD` rejeita alvos que não parecem host (erro 87). O teste de integração usa `TERMSRV/wrapperture-unit-<guid>.invalid` e remove a credencial no fim.
- **DPAPI**: um `hosts.json` copiado de outro usuário ou PC não descriptografa. O caso vira `InvalidTokenException` com orientação para parear de novo, em vez de uma exceção não tratada.
