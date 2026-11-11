# JSON API

The Web UI talks to the backend over HTTP JSON. Kestrel is the only UI host, so
these endpoints are also the way to script the tool: a plain `curl` against a
running instance can import a game, watch progress, and export.

Photino only provides the window — it loads the same origin the browser does.
That is deliberate: every behaviour in the UI is reproducible in a normal
browser while debugging, and nothing is exposed over IPC that isn't an endpoint
here.

## Base URL and lifetime

```
http://127.0.0.1:<port>/
```

The port is chosen when the process starts. Two ways to pin it:

```bash
AssetRipperPatcher --web --port 5199 --no-open
```

`--no-open` keeps the server up without opening a window, which is what the UI
development loop uses. The banner printed at startup contains the final URL:

```
WEB_UI_URL=http://127.0.0.1:5199/
```

Everything is bound to the loopback interface. There is no authentication
because there is no remote surface: the process is a local tool, and the data it
serves is the game the user just pointed it at.

## Conventions

- All request and response bodies are UTF-8 JSON.
- Timestamps are ISO-8601 strings with offset (`2026-11-11T12:58:30.077+08:00`).
- Field names are camelCase (`pathId`, `collectionPath`); they are fixed by the
  DTOs in `Api/ApiModels.cs` and are not derived from internal AssetRipper
  members, so they stay stable even when the submodule is updated.
- A failure that the UI should show uses a 4xx status and a body of
  `{"error": "<message>"}`. `404` from the asset endpoints means "no such
  asset", not "unknown route".
- Long operations (import, export) are **not** synchronous: the POST returns
  immediately with an operation, and progress is polled from `/api/operation`.

## Endpoints

### Environment

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/status` | App version, Unity version, whether a game is loaded, shell kind |
| `GET` | `/api/stats` | Import totals |
| `GET` | `/api/classes` | Class-name histogram for the left panel |
| `GET` | `/api/text` | UI string table for the active language |
| `POST` | `/api/language` | Switch the active language |

`GET /api/status`

```json
{
  "app": "AssetRipperPatcher",
  "version": "1.0.1",
  "unityVersion": "6000.3.21f1",
  "loaded": true,
  "exporting": false,
  "webView2": "157.0.4341.42",
  "shell": "browser",
  "settingsPath": "…\\AssetRipper.Settings.json"
}
```

`unityVersion` and `webView2` are `null` when they do not apply (nothing loaded,
or not running inside WebView2). `exporting` is true while *any* operation runs,
import included, because the UI disables the same controls for both.

`GET /api/stats`

```json
{ "collections": 24, "assets": 56815, "bundles": 3, "resources": 812, "failedFiles": 0, "scenes": 41 }
```

`GET /api/classes` returns an array, sorted by count descending:

```json
[ { "name": "MonoBehaviour", "count": 12102 }, { "name": "GameObject", "count": 10488 } ]
```

`GET /api/text` — returns the table for the *current* language. To switch, post
to `/api/language` first (`{ "language": "en" }`, or `null` to fall back to the
OS locale); the response carries the full string table plus the list of
available languages, so the frontend never needs a second request to build the
language switcher:

```json
{
  "language": "zh-Hans",
  "languages": [ { "code": "zh-Hans", "name": "中文" }, { "code": "en", "name": "English" } ],
  "strings": { "nav.import": "导入", "nav.export": "导出" }
}
```

### Assets

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/assets` | Paged, filtered asset list |
| `GET` | `/api/assets/detail` | One asset: property table + available previews |
| `GET` | `/api/assets/text` | Text content (shader, script, text asset) |
| `GET` | `/api/assets/json` | Raw reflection walk as JSON |
| `GET` | `/api/assets/yaml` | Unity YAML for the asset |
| `GET` | `/api/assets/image` | Decoded texture as an image |
| `GET` | `/api/assets/audio` | Audio payload |
| `GET` | `/api/assets/model` | glTF model |
| `GET` | `/api/assets/font` | Font file |
| `GET` | `/api/assets/video` | Video payload |
| `GET` | `/api/assets/binary` | Raw bytes of a raw data object |
| `GET` | `/api/tree` | Bundle/collection tree |

Every endpoint after the list takes the same two identifiers:

| Parameter | Meaning |
|---|---|
| `path` | The asset's collection path, as returned in `collectionPath` (a JSON string, so URL-encode it) |
| `pathId` | The asset's PathID inside that collection |

`GET /api/assets` query parameters:

| Parameter | Default | Notes |
|---|---|---|
| `q` | — | Substring match on the name |
| `class` | — | Exact class name (`Shader`, `Texture2D`, …) |
| `collection` | — | Exact collection name |
| `sort` | `class` | `name`, `pathId`, `collection`, `class` |
| `desc` | `false` | `true` reverses the sort |
| `offset` | `0` | |
| `limit` | `200` | Clamped to 1…2000 |

```json
{
  "items": [
    {
      "pathId": 7,
      "classId": 48,
      "className": "Shader",
      "name": "Rhythm Doctor/Wave",
      "collection": "globalgamemanagers.assets",
      "collectionPath": "{\"B\":{\"P\":[]},\"I\":23}",
      "originalPath": null,
      "extension": null,
      "sizeBytes": null,
      "width": null,
      "height": null,
      "preview": null,
      "audio": null,
      "model": null,
      "font": null,
      "video": null
    }
  ],
  "total": 352,
  "offset": 0,
  "limit": 200
}
```

When no game is loaded the list endpoints return an empty page rather than an
error, so the UI can call them unconditionally.

`collectionPath` is an opaque handle. Treat it as a string that goes back into
`path` verbatim — it encodes the bundle/collection chain and is not meant to be
parsed by a client.

`GET /api/assets/detail`:

```json
{
  "pathId": 7,
  "classId": 48,
  "className": "Shader",
  "name": "Rhythm Doctor/Wave",
  "collection": "globalgamemanagers.assets",
  "collectionPath": "{\"B\":{\"P\":[]},\"I\":23}",
  "originalPath": null,
  "properties": [ { "name": "类名", "value": "Shader" } ],
  "previews": ["text", "json", "yaml"]
}
```

`previews` tells the UI which tabs to offer; each entry maps to one of the
`/api/assets/*` endpoints above. A tab is only offered when that endpoint can
actually produce something for this asset.

### Operations

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/load` | Start importing |
| `POST` | `/api/export` | Start exporting |
| `POST` | `/api/reset` | Drop the loaded game |
| `GET` | `/api/operation` | Current or last operation |

`POST /api/load`

```json
{ "paths": ["E:\\Game"] }
```

`POST /api/export`

```json
{ "outputPath": "D:\\Exports\\MyGame", "createSubfolder": false }
```

`createSubfolder: true` appends a timestamped folder under `outputPath`, so
repeated exports do not overwrite each other.

Both return `202`-style semantics with an operation body, and both answer `409`
if an operation is already running:

```json
{
  "id": "cf523da56fc04680aad114400683b0e1",
  "kind": "export",
  "state": "running",
  "percent": 0,
  "message": "正在导出",
  "outputPath": null,
  "error": null,
  "startedAt": "2026-11-11T12:58:30.077+08:00",
  "finishedAt": null
}
```

`state` is `running`, `done`, or `failed`. `percent` is 0…100 and only ever
moves forward: the import side maps known import stages to percentages, and the
export side parses the `(n/total)` counter AssetRipper logs, so the bar never
jumps backwards mid-run.

`POST /api/reset` returns `204`.

Poll `GET /api/operation` until `state` is not `running`. There is no
server-sent-events stream; a 600 ms poll is what the UI uses.

### Settings

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/settings` | Current configuration, grouped for the UI |
| `POST` | `/api/settings` | Apply a partial update |

```json
{
  "readOnly": false,
  "groups": [
    {
      "id": "export",
      "items": [
        {
          "key": "Export.ShaderExportMode",
          "label": "Shader 导出模式",
          "description": "导出为 ShaderLab 反编译结果。",
          "type": "enum",
          "value": "Decompile",
          "options": [
            { "value": "Dummy", "label": "虚拟着色器", "description": "…" },
            { "value": "Decompile", "label": "反编译", "description": "…" }
          ]
        }
      ]
    }
  ]
}
```

`type` is `bool`, `text`, or `enum`; it decides which control the UI renders.
Every option carries its own description, which is what lets the UI explain a
setting's effect the moment it is selected.

`readOnly` becomes `true` once a game is loaded: several settings are baked into
the import and cannot be changed without reloading, so the API refuses the
update instead of silently ignoring it.

`POST /api/settings`

```json
{ "values": { "Export.ShaderExportMode": "Decompile", "Processing.EnableAssetDeduplication": "true" } }
```

Values are strings; booleans use `"true"` / `"false"`, enums use the option
value. Returns `409` with `{"error": …}` when the settings are locked (a game is
loaded), and `400` when a key or value is not recognised — the two cases differ
in what the caller should do, so they differ in status.

### Log

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/log` | Buffered log entries |

Two modes, both capped at 2000 entries:

| Mode | Parameters | Result |
|---|---|---|
| Incremental | `since=<seq>`, `limit` (default 500) | Everything after that sequence number |
| Tail | `tail` (default 300) | The last N entries |

```json
{
  "items": [
    { "seq": 66, "time": "12:58:31", "level": "info", "category": "Export", "message": "Starting export" }
  ],
  "lastSeq": 66
}
```

Feed `lastSeq` back as `since` to poll incrementally without re-reading history.

### Native dialogs

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/dialog/folder` | System folder picker |
| `GET` | `/api/dialog/file` | System file picker |

Optional `title` and `basePath` parameters shape the dialog. These block until
the user answers or cancels — that is a property of the native dialog, not a
bug — so a client should not issue another request on the same connection while
one is open.

```json
{ "unsupported": false, "canceled": false, "paths": ["E:\\Game"] }
```

`unsupported: true` means the current environment cannot show a native dialog
(headless, or no desktop session). The UI falls back to asking for the path
directly, which is why the field is a first-class part of the response rather
than an error.

The single-file picker offers one `All files (*.*)` filter. Game inputs come in
too many shapes (`.exe`, `.assets`, `.bundle`, `.apk`, `.zip`, extension-less
resource files) for a list of specific extensions to be anything but a trap.

## Scripted example

```bash
# import, then poll until finished
curl -s -X POST http://127.0.0.1:5199/api/load \
     -H 'Content-Type: application/json' \
     -d '{"paths":["E:\\Game"]}'

while [ "$(curl -s http://127.0.0.1:5199/api/operation | jq -r .state)" = "running" ]; do
  curl -s http://127.0.0.1:5199/api/operation | jq -r '"\(.percent)% \(.message)"'
  sleep 2
done

# list shaders, then read one
curl -s 'http://127.0.0.1:5199/api/assets?class=Shader&limit=5' | jq '.items[].name'

# export
curl -s -X POST http://127.0.0.1:5199/api/export \
     -H 'Content-Type: application/json' \
     -d '{"outputPath":"D:\\Exports\\Game","createSubfolder":true}'
```

## Not exposed

Deliberately, and why:

- **No authentication** — loopback only, local tool.
- **No CORS** — the UI is same-origin; leaving CORS closed keeps a stray page
  from driving the exporter.
- **No arbitrary file read** — every byte-serving endpoint takes an asset
  identity (`path` + `pathId`) and reads it out of the loaded game, never a
  filesystem path.
- **No OpenAPI document** — the surface is small and the DTOs are the contract;
  keeping the schema here avoids a second copy drifting out of date.