# Research: Abrir apps sem alerta e sem barra de idioma

## R1. Alerta de arquivos .rdp (abril de 2026)
- KB5083769 (Win11) / KB5082200 (Win10), CVE-2026-26151: arquivos `.rdp` **não assinados** mostram "Caution: Unknown remote connection" a cada abertura, sem a opção "don't ask again", e com unidades e área de transferência bloqueadas por padrão. Arquivos assinados mostram o publicador. Arquivos de publicadores confiáveis não mostram aviso.
- **Decision**: Assinar com `rdpsign.exe /sha256 <hash-sha256-do-cert> /q <arquivo>` e listar o thumbprint SHA-1 na política *Specify SHA1 thumbprints of certificates representing trusted .rdp publishers*: `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services` → `TrustedCertThumbprints` (REG_SZ, separado por vírgula). Pela descrição da política, arquivos assinados por esses certificados não exibem aviso.
- **Alternatives rejeitadas**:
  - `RedirectionWarningDialogVersion=1`: volta o diálogo antigo para **todos** os `.rdp`, inclusive de phishing, e será removido pela Microsoft.
  - Controle ActiveX do RDP embutido no cliente: reescrita grande.

## R2. Onde fica o certificado (verificado neste PC)
- O `rdpsign` com certificado em `CurrentUser\My` falhou com `0x80092004` (CRYPT_E_NOT_FOUND). Ele procura o certificado em **`LocalMachine\My`**, o que exige admin para criar.
- **Decision**: Criar uma chave CNG de **máquina**, não exportável (`WRApperture-RdpSigning`, 2048 bits, `CngKeyUsages.Signing`). O security descriptor dá `GA` a SYSTEM e Administradores e `GR` aos SIDs dos usuários configurados, para que o usuário comum consiga assinar. O certificado autoassinado (`CN=WRApperture RDP Signing (<MÁQUINA>)`, EKU Code Signing, 10 anos) vai para `LocalMachine\My`.
- **Sem Root nem TrustedPublisher**: a confiança vem só da política de thumbprints. Assim o certificado não vira uma CA confiável para assinatura de código no PC.

## R2b. Validação real neste PC (Windows 11, abril de 2026+), com UAC

Cada linha abaixo foi verificada com captura de tela e UI Automation das janelas do `mstsc`.

| Configuração | Resultado |
|---|---|
| `rdpsign /sha256 <SHA-256 do cert>` | ❌ `0x80092004`: não encontra o certificado |
| `rdpsign /sha256 <thumbprint SHA-1>` | ✅ assina (o nome do parâmetro engana) |
| Assinado + só a política `TrustedCertThumbprints` | ❌ alerta "Fornecedor desconhecido" |
| Assinado + Root + TrustedPublisher + política | ✅ sem alerta |
| Assinado + TrustedPublisher + política (sem Root) | ❌ alerta |
| **Assinado + Root + política (sem TrustedPublisher)** | ✅ **sem alerta** → configuração mínima |
| Certificado com EKU "Remote Desktop Authentication" (crítico ou não) | ❌ `rdpsign` falha com `0x8007000D`: exige o EKU Code Signing |
| **Root com propósito restrito** (`CERT_ENHKEY_USAGE_PROP_ID` = Remote Desktop Authentication) | ✅ RDP sem alerta, e **Authenticode recusado** ("Certificado inválido para o uso solicitado") |
| Arquivo `.rdp` não assinado no mesmo PC | ✅ continua mostrando o alerta |

- **Decision (final)**:
  - Certificado com EKU Code Signing (exigido pelo `rdpsign`) em `LocalMachine\My`, com chave CNG de máquina não exportável e liberada só para os usuários configurados.
  - Cópia pública em `LocalMachine\Root` com os propósitos restritos a Remote Desktop Authentication. Assim, o certificado não pode ser usado para fazer programas parecerem confiáveis.
  - Thumbprint na política `TrustedCertThumbprints`.
  - `TrustedPublisher` não é usado.
- Isso substitui a premissa de R2 ("sem Root"), que se mostrou insuficiente na prática.

## R3. Elevação
- **Decision**: `WRApperture.exe --setup-signing <SID>` e `--remove-signing`, executados via `Verb=runas`. O SID do usuário não elevado é passado como argumento, porque um usuário padrão pode elevar com outra conta de admin.

## R4. Barra de idioma
- **Decision**: `HKU\<SID>\Software\Microsoft\CTF\LangBar` → `ShowStatus` = 3 (oculta). Para usuários comuns: `wrahost langbar hide|show` no HKCU próprio, sem admin. A mudança vale a partir de uma nova sessão.
- **Decision**: O `prepare` aplica a mudança aos hives **carregados** (`S-1-5-21-*`) via a nova seam `IUserRegistryAccess`, com snapshot por SID (`LanguageBarHidden:<SID>`).
