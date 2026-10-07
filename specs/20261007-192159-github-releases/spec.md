# Feature Specification: Releases no GitHub, instaladores gráficos e atualização automática

**Feature Branch**: `20261007-192159-github-releases`

**Created**: 2026-10-07

**Status**: Draft

**Input**: "Quero publicar no GitHub, com as releases, e quero que o app tenha verificação automática de atualização, tanto host quanto client … e é possível fazer um instalador e desinstalador mais bonitinho, sem ser com console de comando?" Repositório: https://github.com/Onutirachi/WRApperture.

Decisões do usuário:

- O instalador é feito com Inno Setup.
- A atualização automática fica ligada por padrão, para que o host se atualize sozinho, já que ninguém vai ficar monitorando o host.
- O cliente mostra que o host está desatualizado e permite atualizá-lo dali.
- A licença é MIT.
- Os dados da empresa e o e-mail do trabalho foram removidos de todo o histórico.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Instalar com assistente (Priority: P1)

O usuário baixa `WRApperture-Client-Setup-<v>.exe` ou `WRApperture-Host-Setup-<v>.exe` da página de releases e instala por um assistente gráfico no idioma do Windows. O assistente tem "Criar atalho na área de trabalho" (marcado por padrão) e "Abrir ao concluir". A desinstalação fica em Configurações → Aplicativos e pergunta se deve apagar os dados (cliente) ou desfazer a preparação do PC (host).

### User Story 2 - O host se atualiza sozinho (Priority: P1)

O agente verifica a última release a cada 6 horas. Com a atualização automática ligada (padrão), ele baixa o instalador do host, confere o SHA-256 e o executa em modo silencioso. O serviço volta com a versão nova, sem ninguém no PC.

### User Story 3 - O cliente se atualiza e avisa sobre hosts antigos (Priority: P1)

O cliente faz a mesma verificação para si mesmo e se atualiza com a opção ligada (padrão). Quando o host selecionado tem versão menor que a última release, o cliente avisa e oferece "Atualizar o PC remoto", que pede ao agente para se atualizar.

### User Story 4 - Releases por tag (Priority: P1)

Uma tag `vX.Y.Z` enviada ao GitHub dispara uma GitHub Action. Ela compila, roda os testes .NET e Pester, gera os dois instaladores e o `SHA256SUMS.txt`, e publica a release com esses arquivos.

## Contrato entre as partes

| Item | Valor |
|---|---|
| Repositório | `Onutirachi/WRApperture` (`https://api.github.com/repos/Onutirachi/WRApperture/releases/latest`) |
| Versão | tag `vX.Y.Z` = `Version` do `Directory.Build.props` = `InformationalVersion` dos executáveis |
| Arquivos da release | `WRApperture-Client-Setup-X.Y.Z.exe`, `WRApperture-Host-Setup-X.Y.Z.exe`, `SHA256SUMS.txt` (`<sha256 hex minúsculo>  <arquivo>` por linha) |
| Instalação silenciosa | `<setup> /VERYSILENT /SUPPRESSMSGBOXES /NORESTART` (o host precisa de administrador; o agente roda como LocalSystem) |
| Cliente instalado em | `%LOCALAPPDATA%\Programs\WRApperture` (por usuário, sem administrador), igual às versões anteriores |
| Host instalado em | `%ProgramFiles%\WRApperture`, igual às versões anteriores |
| Depois de atualizar em silêncio | o instalador do cliente reabre o cliente com `--tray` |

## Requirements *(mandatory)*

- **FR-001**: A atualização só instala arquivos da release oficial, conferidos pelo `SHA256SUMS.txt` da mesma release. Os executáveis não têm assinatura de código, então a autenticidade depende do HTTPS do GitHub e do controle do repositório.
- **FR-002**: Cada verificação no máximo a cada 6 horas (o limite da API sem autenticação é de 60 chamadas por hora por IP). Uma falha de rede só gera log.
- **FR-003**: Opções de atualização automática:
  - Host: `HKLM\SOFTWARE\WRApperture\Agent\AutoUpdate` (DWORD, padrão 1), alterável no app do host.
  - Cliente: `hosts.json`, alterável no menu ⋯.
- **FR-004**: O agente expõe `GET /api/update` (versão atual e última, se há atualização, se está no automático) e `POST /api/update` (atualizar agora), protegidos pelo token. `/api/host` anuncia o recurso `self-update`.
