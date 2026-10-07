# Implementation Plan: Host Agent

**Branch**: `20261007-105635-host-agent` | **Date**: 2026-10-07 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/20261007-105635-host-agent/spec.md`

## Summary

O serviço `WRApperture.Host.Agent` (ASP.NET Core minimal API sobre Kestrel HTTPS, rodando como Windows Service) expõe `/api/host`, `/api/apps` e `/api/apps/{alias}/icon`. A autenticação é por token com hash e salt guardados no HKLM e tem limite de falhas por IP. Os DTOs ficam no novo projeto `WRApperture.Shared`, que o cliente também vai usar. O Core ganha `IAgentSettingsStore` e `IHostInfoProvider`, e o `wrahost` ganha o grupo de comandos `agent`. Os detalhes estão em [research.md](research.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (`net8.0-windows10.0.17763.0`)

**Primary Dependencies**: ASP.NET Core 8 (framework), `Microsoft.Extensions.Hosting.WindowsServices` 8, `Serilog.AspNetCore` 8, `Serilog.Sinks.EventLog`. Para testes, `Microsoft.AspNetCore.Mvc.Testing` 8.

**Storage**: HKLM `SOFTWARE\WRApperture\Agent` (via Core) e repositório de certificados `LocalMachine\My`.

**Testing**: xUnit. Testes do Core com `FakeRegistryAccess`. Testes do Agent com `WebApplicationFactory` e fakes.

**Target Platform**: Windows 10/11 Pro+, serviço LocalSystem.

**Project Type**: Web service.

**Performance Goals**: `/api/apps` em menos de 100 ms no servidor (SC-001 fala em menos de 1 s, ponta a ponta). Ícones servidos do cache.

**Constraints**: Somente HTTPS. Nenhum caminho de executável exposto. Token nunca logado.

**Scale/Scope**: 1 a 5 clientes por host.

## Constitution Check

| Princípio | Status | Evidência |
|---|---|---|
| I. Stack | ✅ | ASP.NET Core 8. Dependências justificadas em R1 e R8 |
| II. Registro via Core | ✅ | As Agent Settings e a porta RDP são lidas e gravadas por `IAgentSettingsStore`/`IHostInfoProvider` no Core, com testes de fake |
| III. Idempotente/reversível | ✅ | O certificado só é recriado quando é necessário. Regenerar o token é a única operação destrutiva, e é explícita |
| IV. Segurança | ✅ | HTTPS obrigatório, token com hash e salt e comparação em tempo constante, limite de falhas, fingerprint SHA-256 para pinning, nenhum caminho exposto |
| V. Observabilidade | ✅ | Serilog em arquivo e no Event Log. Sem banco de dados |

**Re-check pós-design**: ✅

## Project Structure

```text
src/
├── WRApperture.Shared/                 # NOVO: ApiContract (rotas, header), DTOs
├── WRApperture.Host.Core/
│   ├── Agent/                        # NOVO: AgentSettings, IAgentSettingsStore, AgentSettingsStore, TokenHasher
│   └── HostInfo/                     # NOVO: IHostInfoProvider, HostInfoProvider
├── WRApperture.Host.Agent/             # NOVO
│   ├── Program.cs                    # host, Kestrel HTTPS, Serilog, endpoints
│   ├── Security/TokenAuthMiddleware.cs, FailedAuthLimiter.cs
│   ├── Certificates/AgentCertificateManager.cs
│   ├── Endpoints/ApiEndpoints.cs
│   └── Icons/IconCache.cs
└── WRApperture.Host.Cli/AgentCommands.cs   # NOVO
tests/
├── WRApperture.Host.Core.Tests/Agent/   # AgentSettingsStoreTests, HostInfoProviderTests
└── WRApperture.Host.Agent.Tests/        # NOVO: ApiTests (WebApplicationFactory)
```

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Projeto `WRApperture.Shared` | O contrato de API é compartilhado entre o Agent e o Client | Duplicar os DTOs causaria divergência silenciosa do contrato |
