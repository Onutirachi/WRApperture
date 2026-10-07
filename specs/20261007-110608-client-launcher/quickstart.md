# Quickstart: validar o Client Launcher

## Pré-requisitos
- Host com `host-core` e `host-agent` validados (quickstarts anteriores): agente rodando, token gerado e pelo menos 2 apps publicados (ex.: Bloco de Notas e Calculadora).
- PC cliente na mesma rede, com o .NET 8 Desktop Runtime ou o build self-contained.

## 1. Testes
```powershell
dotnet test tests/WRApperture.Client.Tests
```

## 2. Rodar o cliente
```powershell
dotnet run --project src/WRApperture.Client
```

## 3. Cenários
1. **Parear**: clique em "Adicionar host" e informe o IP do host, a porta 5080 e o token. O fingerprint exibido deve ser igual ao de `wrahost agent show`. Confirme.
2. **Grade**: os 2 apps aparecem com ícone. Digite "calc" na busca e só a Calculadora deve ficar visível.
3. **Abrir**: clique no Bloco de Notas, informe as credenciais do host (se pedir) e confira que só a janela dele aparece. Em seguida clique na Calculadora: ela abre sem pedir login, com as duas janelas abertas.
4. **Credenciais**: use o menu do host → "Salvar credenciais...", feche os apps e abra de novo. Nenhuma tela de login deve aparecer.
5. **Atalho**: clique com o botão direito no app → "Criar atalho na área de trabalho". Feche o cliente e dê duplo clique no atalho.
6. **Offline**: pare o agente e reabra o cliente. A lista deve aparecer com o aviso "host offline".
7. **Revogação**: rode `wrahost agent token` no host e clique em atualizar no cliente. Deve aparecer "token inválido" com a opção de informar o novo token.

## 4. Verificações de segurança
```powershell
Select-String -Path "$env:LOCALAPPDATA\WRApperture\client\hosts.json" -Pattern "<TOKEN>"   # nenhum resultado
Select-String -Path "$env:LOCALAPPDATA\WRApperture\logs\*.log" -Pattern "<TOKEN>"          # nenhum resultado
```
