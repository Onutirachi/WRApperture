# Data Model: Packaging

Não há novas entidades persistentes. A feature usa:

- **Agent Settings** (host-agent): lidas por `wrahost agent show --json` e apagadas por `wrahost agent reset` (`IAgentSettingsStore.Reset()`, novo, requer admin).
- **Serviço `WRAppertureAgent`**: `binPath="<InstallDir>\WRApperture.Agent.exe"` (entre aspas), conta LocalSystem, início automático e recuperação restart 5 s / 10 s / 60 s.
- **Regra de firewall `WRAppertureAgent-TCP`**: entrada, TCP, porta = `Agent Settings.Port`, perfis Domain e Private (Public opcional).
- **PATH da máquina**: `<InstallDir>` é acrescentado uma única vez e removido na desinstalação.
