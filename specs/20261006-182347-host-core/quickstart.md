# Quickstart: validar o Host Core

## Pré-requisitos
- .NET 8 SDK.
- Host Windows 10/11 Pro, Enterprise ou Education, com um terminal **como administrador** para os comandos de escrita.
- Um segundo PC ou VM na mesma rede, com uma conta do host que tenha permissão de RDP.

## 1. Testes automatizados
```powershell
dotnet test tests/WRApperture.Host.Core.Tests
```
Esperado: todos verdes. Os testes usam fakes de registro, firewall e sistema de arquivos, sem tocar no sistema real.

## 2. Diagnóstico e preparação (no host, elevado)
```powershell
dotnet run --project src/WRApperture.Host.Cli -- check
dotnet run --project src/WRApperture.Host.Cli -- prepare
dotnet run --project src/WRApperture.Host.Cli -- check     # todos OK
dotnet run --project src/WRApperture.Host.Cli -- prepare   # sem alterações (idempotente)
```

## 3. Publicar o Bloco de Notas
```powershell
dotnet run --project src/WRApperture.Host.Cli -- publish --path "%SystemRoot%\System32\notepad.exe" --name "Bloco de Notas"
dotnet run --project src/WRApperture.Host.Cli -- list
```
Esperado: alias `bloco-de-notas`. Em `regedit`, a chave `...\TSAppAllowList\Applications\bloco-de-notas` deve existir com os valores da [data-model](data-model.md).

## 4. Abrir do cliente
Crie `notepad.rdp` no cliente:
```
full address:s:<IP-OU-NOME-DO-HOST>
remoteapplicationmode:i:1
remoteapplicationprogram:s:||bloco-de-notas
remoteapplicationname:s:Bloco de Notas
disableremoteappcapscheck:i:1
```
Dê duplo clique, informe as credenciais do host. Esperado: só a janela do Bloco de Notas aparece no cliente.
Repita com um segundo app publicado. Esperado: ele abre na mesma sessão, sem nova autenticação.

## 5. Descoberta e ícone
```powershell
dotnet run --project src/WRApperture.Host.Cli -- discover
dotnet run --project src/WRApperture.Host.Cli -- icon bloco-de-notas --out $env:TEMP\np.png
```
Esperado: a lista sem duplicatas e sem desinstaladores, e um PNG de 48x48 válido.

## 6. Limpeza
```powershell
dotnet run --project src/WRApperture.Host.Cli -- unpublish bloco-de-notas
dotnet run --project src/WRApperture.Host.Cli -- revert
dotnet run --project src/WRApperture.Host.Cli -- check     # volta ao estado original
```

## 7. Sem elevação
Em um terminal comum, `publish` deve sair com código `2` e a mensagem "requer administrador". Já `list` e `check` devem funcionar normalmente.
