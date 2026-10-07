# Research: Host Agent

## R1. Hospedagem
- **Decision**: ASP.NET Core 8 minimal API, com Kestrel e `Microsoft.Extensions.Hosting.WindowsServices` (`UseWindowsService`). O agente roda como serviço `LocalSystem`, que pode ler o HKLM e gravar no repositório de certificados da máquina. O mesmo binário também roda em console, para desenvolvimento.
- **Alternatives**: `HttpListener` (exige `netsh http add urlacl` e certificado vinculado via `netsh http add sslcert`, mais frágil) e gRPC (o cliente WPF teria mais dependências sem ganho). Ambos rejeitados.

## R2. Token de pareamento
- **Decision**:
  - Token: 32 bytes aleatórios (`RandomNumberGenerator`) codificados em Base64Url, com 43 caracteres.
  - Armazenamento: `SHA-256(salt ‖ UTF8(token))` com salt aleatório de 16 bytes, gravados em `HKLM\SOFTWARE\WRApperture\Agent` (`TokenHash` e `TokenSalt` como REG_BINARY).
  - Verificação com `CryptographicOperations.FixedTimeEquals`.
  - O agente relê o hash a cada requisição, com cache de 5 s, para que a regeneração valha sem reiniciar.
- **Rationale**: Com 256 bits de entropia, um KDF lento (PBKDF2) não acrescenta segurança e só aumentaria a latência. O salt impede tabelas pré-computadas. A leitura no HKLM segue o princípio II: passa por `IAgentSettingsStore` no Core.
- **Header**: `X-WRApperture-Token`.

## R3. HTTPS e certificado
- **Decision**:
  - Certificado autoassinado RSA 2048/SHA-256 com `CN=WRApperture Agent (<MACHINE>)`, SAN com DNS `<MACHINE>` e `localhost`, EKU serverAuth e validade de 5 anos.
  - É gravado em `LocalMachine\My` com `MachineKeySet | PersistKeySet`. O thumbprint (SHA-1, que identifica o certificado no store) fica nas Agent Settings.
  - Na inicialização: se o certificado estiver ausente, faltando a chave privada ou vencendo em menos de 30 dias, um novo é gerado.
  - **Fingerprint** exposto ao cliente: SHA-256 do DER em hex maiúsculo, que é o valor que o cliente fixa.
- **Rationale**: Atende o princípio IV (HTTPS por padrão e pinning) sem depender de uma CA.
- **Note**: O certificado é carregado dentro de `ConfigureKestrel`/`UseHttps`, que só roda com Kestrel real. Os testes com `TestServer` não tocam no repositório de certificados.

## R4. Limite de tentativas
- **Decision**: Um middleware próprio conta as falhas de autenticação por IP numa janela deslizante (10 falhas em 5 min resultam em 429 por 5 min). O estado fica em memória (`ConcurrentDictionary` com limpeza oportunista).
- **Alternatives**: `Microsoft.AspNetCore.RateLimiting` limita *todas* as requisições, não só as falhas. Atender a esse caso exigiria lógica customizada de qualquer forma, por isso foi rejeitado.

## R5. Porta RDP e nome do host
- **Decision**: A porta RDP é lida de `RDP-Tcp\PortNumber` (padrão 3389), via Core (`IHostInfoProvider`). O nome do host é `Environment.MachineName`. O cliente pode sobrescrever o endereço, por exemplo com o IP.

## R6. Ícones
- **Decision**: `GET /api/apps/{alias}/icon?size=48` usa o `IIconProvider` do Core. O resultado fica em cache em memória por (alias, IconPath, IconIndex, size, LastWriteTime do arquivo de ícone). O ETag é o SHA-256 truncado do PNG e o header é `Cache-Control: private, max-age=3600`. Com `If-None-Match` igual, a resposta é 304.

## R7. Testes
- **Decision**: `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`). Os serviços do Core são substituídos por fakes via `ConfigureTestServices`.

## R8. Logging
- **Decision**: `Serilog.AspNetCore`, gravando em arquivo em `%ProgramData%\WRApperture\logs\agent-.log` e, em modo serviço, também no Event Log. O token nunca é logado; nas falhas de autenticação, só o IP de origem.
