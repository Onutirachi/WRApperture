# WRApperture Constitution

## Core Principles

### I. Plataforma e stack únicas
Todo código é C# sobre **.NET 8** (LTS). As interfaces gráficas usam **WPF com MVVM** (CommunityToolkit.Mvvm) e o agente usa **ASP.NET Core minimal API**. Os alvos são o host Windows 10/11 Pro/Enterprise e o cliente Windows 10/11. Dependências novas precisam de justificativa no `plan.md` da feature.

### II. O registro só é alterado pela camada Core (NON-NEGOTIABLE)
Toda leitura ou escrita em `TSAppAllowList` e nas chaves de Terminal Server passa por `WRApperture.Host.Core`, por trás de uma abstração (`IRegistryAccess`) que pode ser substituída por um fake. Nenhuma UI, serviço ou script grava no registro diretamente. Toda operação de Core tem teste unitário com o registro fake.

### III. Operações idempotentes e reversíveis
Publicar, despublicar e preparar o host podem ser executados repetidas vezes sem efeito colateral. Toda alteração de configuração do sistema (RDP, firewall, chaves de registro) tem uma operação inversa documentada. Nada é removido fora do escopo do próprio app (por exemplo, apagar `TSAppAllowList` inteira é proibido).

### IV. Segurança por padrão
- Nenhum segredo (token, senha) é guardado em texto puro. No cliente, use DPAPI (CurrentUser). No host, guarde apenas o hash (SHA-256 + salt) do token.
- O agente HTTP exige token em toda rota `/api/*` e usa HTTPS por padrão.
- O cliente fixa o certificado do host (thumbprint) no primeiro pareamento.
- Os caminhos de executável publicados são validados (existem, são absolutos e não estão em pasta gravável por usuário comum sem aviso).

### V. Observabilidade e simplicidade
Os logs são estruturados com Serilog, em arquivo rotativo em `%ProgramData%\WRApperture\logs` (host) ou `%LOCALAPPDATA%\WRApperture\logs` (cliente). Comece pelo mais simples que resolve (YAGNI): nada de banco de dados, já que o próprio registro é a fonte da verdade dos apps publicados.

## Restrições adicionais

- A interface do usuário e a documentação de usuário são em **português (PT-BR)**. Código, identificadores e logs são em inglês.
- Windows Pro permite **uma sessão RDP simultânea**. Abrir um RemoteApp desconecta quem estiver logado localmente no host, e as specs e a UI devem deixar isso claro.
- A distribuição é por `dotnet publish` self-contained, em um único arquivo por app.

## Fluxo de desenvolvimento

- SDD com Spec Kit: `/speckit-specify` → (`/speckit-clarify`) → `/speckit-plan` → `/speckit-tasks` → (`/speckit-analyze`) → `/speckit-implement`.
- As features usam ID por **timestamp** (`YYYYMMDD-HHMMSS-<slug>`), para que várias pessoas criem specs em paralelo sem conflito de numeração.
- Uma branch por feature, com o mesmo nome da pasta em `specs/`.
- Para dar merge, `dotnet build` precisa passar sem warnings novos e `dotnet test` precisa ficar verde.

## Governance

Esta constituição prevalece sobre outras práticas do projeto. Alterações exigem PR próprio atualizando versão e data, e os `plan.md` de features abertas devem revalidar o "Constitution Check". O versionamento segue SemVer: MAJOR para remover ou redefinir princípio, MINOR para adicionar, PATCH para redação.

**Version**: 1.0.0 | **Ratified**: 2026-10-06 | **Last Amended**: 2026-10-06
