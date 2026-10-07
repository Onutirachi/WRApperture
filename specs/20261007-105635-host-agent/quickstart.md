# Quickstart: validar o Host Agent

## 1. Testes
```powershell
dotnet test
```

## 2. Configurar e rodar (host, terminal **como administrador**)
```powershell
$wrahost = "src\WRApperture.Host.Cli\bin\Debug\net8.0-windows10.0.17763.0\wrahost.exe"
& $wrahost agent token          # anote o token (exibido uma vez)
& $wrahost publish --path "%SystemRoot%\System32\notepad.exe" --name "Bloco de Notas"
netsh advfirewall firewall add rule name="WRApperture Agent" dir=in action=allow protocol=TCP localport=5080
dotnet run --project src/WRApperture.Host.Agent
```
Esperado: o log mostra "Agent listening on https://*:5080" e o fingerprint. Na primeira execução, o certificado é criado em `certlm.msc` → Pessoal.

## 3. Consultar (do próprio host ou de outro PC)
```powershell
$h = @{ 'X-WRApperture-Token' = '<TOKEN>' }
curl.exe -k https://localhost:5080/health                                   # ok
curl.exe -k -H "X-WRApperture-Token: <TOKEN>" https://localhost:5080/api/host
curl.exe -k -H "X-WRApperture-Token: <TOKEN>" https://localhost:5080/api/apps
curl.exe -k -H "X-WRApperture-Token: <TOKEN>" https://localhost:5080/api/apps/bloco-de-notas/icon -o np.png
curl.exe -k https://localhost:5080/api/apps                                 # 401
```

## 4. Revogação
`wrahost agent token` gera um novo token. Repita a consulta com o token antigo: a resposta deve ser 401, sem reiniciar o agente.

## 5. Limpeza
Pare o agente (Ctrl+C) e rode `netsh advfirewall firewall delete rule name="WRApperture Agent"`.
