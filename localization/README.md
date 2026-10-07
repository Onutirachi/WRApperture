# Translations

Each language is a folder named after a .NET culture: `en`, `pt`, `es`, `fr`… A regional variant such as `pt-PT` is also allowed. Each folder has one JSON file per area:

| File | Used by |
|---|---|
| `common.json` | shared UI pieces (theme menu…) |
| `client.json` | WRApperture (client app) |
| `manager.json` | WRApperture (Host), the host manager window |
| `host.json` | host core messages (readiness checks, publishing, validation), shown by the manager and `wrahost` |
| `cli.json` | `wrahost` command line |
| `agent.json` | host agent messages |
| `installer.json` | `Instalar.cmd` / `Desinstalar.cmd` scripts |

Each file is a flat JSON object. The key is short and the text is the translation. In code, a key is written as `<area>.<key>`, e.g. `common.theme.dark`. `{0}`, `{1}`… are placeholders and must be kept in every translation.

## Adding a language

1. Copy the `en` folder and rename the copy to the language code, e.g. `es`.
2. Translate the texts (the values) and leave the keys alone.
3. Rebuild. The new language is picked up automatically: there is no list to edit.

Everyone whose Windows display language matches gets it automatically:

- `es-MX` uses `es-MX`, then `es`, then `en`.
- Any text missing from a language falls back to the next language in that chain.
- The tests check that every language has the same keys and placeholders as `en`, and that every key used in the code exists in `en`.

## Choosing the language

The language follows the Windows display language. To force one, for example to test a translation, either:

- set the environment variable `WRAPPERTURE_LANG=es`, or
- set the registry value `HKCU\Software\WRApperture\Ui\Language` (string) to `es`.
