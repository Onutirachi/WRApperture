# Contract: API pública do WRApperture.Host.Core

Esta é a API consumida pelo Manager, pelo Agent e pelo CLI `wrahost`. Mudanças nela que quebrem compatibilidade exigem atualizar este contrato.

```csharp
public interface IAppPublisher
{
    IReadOnlyList<PublishedApp> List();                       // nunca exige admin
    PublishedApp? Get(string alias);
    PublishResult Publish(PublishRequest request);            // admin; idempotente
    bool Unpublish(string alias);                             // admin; false se não existia
}

public interface IHostPreparation
{
    IReadOnlyList<ReadinessCheck> Check();                    // nunca exige admin
    IReadOnlyList<ReadinessCheck> Prepare();                  // admin; idempotente; retorna estado final
    IReadOnlyList<ReadinessCheck> Revert();                   // admin; usa snapshot
}

public interface IAppDiscovery
{
    IReadOnlyList<DiscoveredApp> Discover();
}

public interface IIconProvider
{
    byte[] GetPng(string iconPath, int iconIndex, int size = 48); // nunca lança: fallback genérico
}
```

## Seams de teste (internos, substituíveis via DI)
```csharp
public interface IRegistryAccess      // HKLM somente
{
    bool KeyExists(string path);
    IReadOnlyList<string> GetSubKeyNames(string path);
    object? GetValue(string path, string name);
    void SetValue(string path, string name, object value, RegistryValueKind kind);
    void DeleteValue(string path, string name);
    void DeleteKey(string path);                 // só a subchave indicada, nunca recursivo acima dela
}
public interface IFirewallRules { IReadOnlyList<(string Name, bool Enabled)> GetRdpRules(); void SetEnabled(string name, bool enabled); }
public interface IPrivilegeChecker { bool IsAdministrator(); }
public interface IFileSystemProbe { bool FileExists(string path); string Expand(string path); bool IsUserWritableLocation(string path); }
```

## Erros
| Exceção | Quando |
|---|---|
| `AdminRequiredException` | Escrita sem elevação (FR-013) |
| `AppValidationException` (com `Field`, `Reason`) | Caminho inexistente/relativo, alias inválido, `Required` sem argumentos (FR-003/004) |
| `HostNotSupportedException` | `Prepare`/`Revert` em edição Home |

## Registro de DI
`services.AddWRAppertureHostCore()` registra todas as implementações reais (singleton).

## CLI `wrahost` (contrato de linha de comando)
```
wrahost list [--json]
wrahost publish --path <exe> --name <nome> [--alias <a>] [--args <s>] [--args-policy none|any|required] [--icon <p>] [--icon-index <n>]
wrahost unpublish <alias>
wrahost check [--json]
wrahost prepare
wrahost revert
wrahost discover [--json]
wrahost icon <alias> --out <arquivo.png> [--size 48]
```
Códigos de saída: `0` sucesso, `1` erro de validação, `2` requer administrador, `3` host não suportado.
