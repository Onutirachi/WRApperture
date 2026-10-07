# Quickstart: validar o Packaging

## 1. Gerar os pacotes (máquina de desenvolvimento)
```powershell
.\scripts\build-release.ps1
Get-ChildItem dist\*.zip
```

## 2. Testes dos scripts
```powershell
Invoke-Pester tests\packaging
```

## 3. Host (VM ou PC Windows Pro, sem .NET)
1. Extraia `WRApperture-Host-<v>.zip` e dê duplo clique em `Instalar.cmd` (aceite o UAC).
2. Esperado: o resumo mostra "Agente respondendo", o endereço IP, a porta, o **token** e o fingerprint.
3. Publique um app: `wrahost publish --path "%SystemRoot%\System32\notepad.exe" --name "Bloco de Notas"` (o `wrahost` passa a estar no PATH em um novo terminal).
4. Reinicie o PC e, sem fazer login, consulte de outro PC: `curl.exe -k https://<ip>:5080/health` deve retornar `ok`.
5. Rode o `Instalar.cmd` de novo. Esperado: "Token existente mantido", e o cliente continua funcionando sem reparear.

## 4. Cliente (outro PC, sem .NET)
1. Extraia `WRApperture-Client-<v>.zip` e dê duplo clique em `Instalar.cmd`.
2. Abra "WRApperture" pelo Menu Iniciar e pareie com o IP e o token do passo 3.2.

## 5. Desinstalação
- Host: duplo clique em `C:\Program Files\WRApperture\Desinstalar.cmd` e responda "N" (apps e token preservados). Depois reinstale e desinstale respondendo "s": o `wrahost check` deve voltar ao estado original.
- Cliente: Menu Iniciar → "Desinstalar WRApperture" e responda "s" para apagar também os dados.
