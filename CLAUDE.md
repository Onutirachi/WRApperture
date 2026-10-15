# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

WRApperture opens individual apps from a Windows PC (the **host**) on another computer (the **client**), each in its own window, using native RDP **RemoteApp** without an RDS server. It works on Windows 10/11 Pro/Enterprise/Education. Windows Pro allows **one session at a time**, so opening a RemoteApp disconnects whoever is using the host locally. Specs and UI must keep that visible.

## Commands

All .NET projects target `net8.0-windows10.0.17763.0` (set in `Directory.Build.props`, along with `TreatWarningsAsErrors`, nullable and C# 12). A build with any warning fails.

```powershell
dotnet build                                          # whole solution (WRApperture.sln)
dotnet test                                           # unit + integration
dotnet test --filter "Category!=Integration"          # unit only (integration = real icons/shell, read-only)
dotnet test tests/WRApperture.Host.Core.Tests --filter "FullyQualifiedName~AppPublisherTests"   # one class
dotnet test --filter "FullyQualifiedName~AppPublisherTests.MethodName"                          # one test
Invoke-Pester tests\packaging                         # installer scripts: Windows PowerShell 5.1, Pester 3/4 syntax
.\scripts\build-release.ps1 [-SkipTests]              # dist\WRApperture-{Host,Client}-Setup-<v>.exe + SHA256SUMS.txt (needs Inno Setup 6)
.\scripts\build-icons.ps1                             # regenerate .ico/.png/.xaml from assets/icons/*.svg
```

To run the apps from source:
- `dotnet run --project src/WRApperture.Host.Agent` needs an **elevated** terminal. On first run it creates a self-signed cert in `LocalMachine\My`.
- `dotnet run --project src/WRApperture.Client`.
- The CLI is at `src\WRApperture.Host.Cli\bin\Debug\net8.0-windows10.0.17763.0\wrahost.exe`, with the verbs `check`, `prepare`, `revert`, `discover`, `publish`, `list`, `unpublish`, `agent token|show`, `sessions` and `langbar`. Most verbs need admin.
- CLI exit codes: `0` ok, `1` validation, `2` admin required, `3` unsupported Windows edition.

CI (`.github/workflows/ci.yml`) runs `dotnet test -c Release` plus Pester on `windows-latest`.

## Architecture

Projects and their dependencies (`src/`):

- **WRApperture.Shared**: the HTTP API contract between agent and client (`ApiContract.cs`: routes, the `X-WRApperture-Token` header, DTOs, error codes), certificate fingerprints, localization (`Strings`) and the self-update machinery (`Updates/`). Every other project references it.
- **WRApperture.Host.Core**: all host-side system logic.
  - Publishing apps means writing the registry under `TSAppAllowList`.
  - Also host preparation (RDP, NLA, firewall, language bar), app discovery, icon extraction, session policy and the agent settings store.
  - Real Windows implementations sit behind interfaces in `Abstractions/` and are implemented in `Platform/`. They are registered by `AddWRAppertureHostCore()`.
  - Tests swap them for fakes (`tests/WRApperture.Host.Core.Tests/Fakes`).
- **WRApperture.Host.Cli** (`wrahost.exe`): System.CommandLine front end over Core. The installers drive host setup through it, e.g. `wrahost prepare`.
- **WRApperture.Host.Agent** (`WRApperture.Agent.exe`): an ASP.NET Core minimal API that runs as the `WRAppertureAgent` Windows service over HTTPS on port 5080.
  - `TokenAuthMiddleware` guards `/api/*`. The token is stored only as a salted SHA-256 hash, and `FailedAuthLimiter` throttles failed attempts.
  - Background services: `SessionWatcher` (session handoff when a user logs on locally while RemoteApps are open) and `HostUpdater` (self-update even when no one is logged on).
  - Tests use `WebApplicationFactory` with the `Testing` environment. That skips Kestrel/cert-store setup and file logging (see `Program.cs`).
- **WRApperture.Host.Manager** (`WRApperture.Host.exe`): the host's WPF GUI.
  - It calls Core directly and only manages the agent service (`AgentService`). It does not go through the HTTP API.
  - It relaunches itself elevated (`Elevation`), or runs with `--read-only`.
- **WRApperture.Client.Core**: client logic with no UI.
  - `AgentClient` pairs with the host and pins its certificate (`CertificatePinValidator`).
  - `HostStore`/`IconStore` hold the offline cache under `%LOCALAPPDATA%\WRApperture\client`.
  - Also: `.rdp` generation (`RdpFileBuilder`) and signing (`Signing/`, to avoid Windows' unsigned-.rdp warning), launching via `mstsc` (`RemoteAppLauncher`), DPAPI-protected token storage, Credential Manager passwords, and desktop/Start-menu shortcuts.
- **WRApperture.Client** (`WRApperture.exe`): the client's WPF MVVM app (CommunityToolkit.Mvvm). It has a tile grid, a tray icon and single-instance handling.
- **WRApperture.Ui**: the shared WPF theme (light/dark `Colors.*.xaml`, `ThemeManager`), the language picker and the `{T key}` XAML markup extension (`TExtension`).

Partial classes named `*.Updates.cs` (`MainViewModel`, `ManagerViewModel`) hold the auto-update UI.

### Cross-cutting rules (from `.specify/memory/constitution.md`)

- **Registry writes to `TSAppAllowList` and Terminal Server keys happen only in Host.Core**, through `IRegistryAccess`. This is non-negotiable. No UI, service or script writes them directly. Every Core operation gets a unit test against the fake registry.
- Publish, unpublish and prepare are **idempotent and reversible**. `revert` undoes `prepare`. Never delete anything outside the app's own scope (for example, never all of `TSAppAllowList`).
- No plaintext secrets: DPAPI (CurrentUser) on the client, a salted hash on the host. Validate published exe paths: absolute, existing, and a warning if they are under a user-writable folder.
- Logging uses Serilog to rolling files: `%ProgramData%\WRApperture\logs` on the host, `%LOCALAPPDATA%\WRApperture\logs` on the client. There is no database: the registry is the source of truth for published apps.
- **User-facing text and user docs are PT-BR. Code, identifiers and logs are in English.**
- New dependencies need a justification in the feature's `plan.md`.

### Localization

- All UI strings live in `localization/<culture>/<area>.json`: flat JSON, referenced in code as `area.key`, with `{0}` placeholders.
- They are embedded into `WRApperture.Shared` and read by `Strings`. The installer scripts read the same files through `packaging/Localization.ps1`.
- When adding or changing a string, update **both** `en` and `pt`. Tests check that every language has the same keys and placeholders as `en`, and that every key used in code exists in `en`.
- Test assemblies pin English through a `TestLanguage` module initializer, so assert English messages.
- Force a language with `WRAPPERTURE_LANG=pt`.

### Packaging, releases and updates

- `packaging/installer/*.iss` holds the Inno Setup graphical installers.
- `packaging/{host,client}/*.ps1` + `Instalar.cmd` are the legacy/automation scripts. They still work and support `-DryRun`.
- PowerShell scripts must stay compatible with Windows PowerShell 5.1 and be saved as UTF-8 **with BOM** (`.editorconfig`), or the PT-BR text gets garbled. `.cmd` files must keep CRLF line endings.
- To release:
  1. Bump `<Version>` in `Directory.Build.props`.
  2. Push the tag `v<version>`. `release.yml` checks that the tag matches the version, builds the setups and publishes the GitHub release.
- The installed apps update from the latest release of `Onutirachi/WRApperture` (`Shared/Updates/UpdateSource.cs`). A setup runs only if its SHA-256 matches `SHA256SUMS.txt`.

## Workflow (Spec Kit / SDD)

- Features follow `/speckit-specify` → (`/speckit-clarify`) → `/speckit-plan` → `/speckit-tasks` → (`/speckit-analyze`) → `/speckit-implement`.
- Each feature lives in `specs/<YYYYMMDD-HHMMSS>-<slug>/` and its branch has the same name. The timestamp IDs avoid collisions.
- Specs are the reference for behavior, e.g. `specs/20261007-105635-host-agent/contracts/http-api.md` for the API and `specs/20261006-182347-host-core/quickstart.md` for end-to-end validation.
- Ideas wait in `specs/BACKLOG.md`.
- C# style: file-scoped namespaces (enforced as a warning, which fails the build) and `System` usings first.

## Git conventions

These follow the constitution and the commit history.

- **Branches.** One branch per feature, created from `main`, named exactly like its spec folder (`YYYYMMDD-HHMMSS-<slug>`). `/speckit-specify` or `/speckit-git-feature` creates it.
  - Changes that are not part of the feature go on their own branch with their own timestamped name, even small ones. Examples: repository docs such as this file, tooling, unrelated fixes.
  - Never mix them into a feature branch.
- **Commit messages.** Use Conventional Commits, in English: `type(scope): summary`.
  - The summary is lowercase and says what changed for the user, e.g. `feat(client): add apps to the Start menu`.
  - Types: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`.
  - The scope is the area touched (`client`, `host`, `host-manager`, `agent`, `packaging`, `localization`, `update`, `ui`, `release`…) or the feature slug.
  - Larger commits add a body summarizing the change per component, e.g. "Shared: … Agent: … Host app: … Client: …".
- **What a feature commit holds.** A feature usually lands as one `feat` commit that includes its spec, tests, translations (`en` and `pt`) and doc updates. A commit with only the spec is `docs(<slug>): spec, plan and tasks`.
- **Merging.** Keep `main` linear: rebase or fast-forward a feature branch onto `main`, with no merge commit. Before merging, `dotnet build` must have no warnings, and `dotnet test` and `Invoke-Pester tests\packaging` must pass.
- **Releases.** These are separate from features. On `main`:
  1. Bump `<Version>` in `Directory.Build.props` in its own commit, `chore(release): vX.Y.Z`.
  2. Tag it `vX.Y.Z`. The Release workflow fails if the tag and `<Version>` differ.
- **Commit and push** only when the user asks.
