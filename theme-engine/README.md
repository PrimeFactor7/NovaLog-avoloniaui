# Theme Engine

VS Code–compatible theme engine: import themes from the Marketplace via a proxy (Node or .NET), map tokens to app CSS variables, and edit overrides with live preview.

## Architecture (do not alter)

1. **Proxies** – Bypass CORS and VSIX limits: fetch extension, unzip, extract theme JSON/JSONC, strip comments, return token dictionaries.
   - **Node proxy** (`server/`) – Experiment; port 3001.
   - **.NET proxy** (`ThemeProxy/`) – Production; port 5000; single-file publish; can be run as a sidecar by NovaLog on Windows.
2. **React frontend** (`client/`) – Fetches from proxy, maps VS Code keys → app CSS vars via `mapVSCodeToAppTokens`, keeps `baseTheme` + `userOverrides`; deep-merge (overrides win) and injects into `document.documentElement` as CSS variables.
3. **Fallback CSS** – All components use `var(--token, var(--default-token))` so missing keys don’t break the UI.

## Run

### Option 1: Node proxy (experiment)

```bash
# Terminal 1: Node proxy (port 3001)
cd theme-engine/server && npm install && npm start

# Terminal 2: client (Vite proxies /api to localhost:3001)
cd theme-engine/client && npm install && npm run dev
```

### Option 2: .NET proxy (production)

Build and run the .NET proxy, then point the client at it.

```bash
# Build and run .NET proxy (port 5000)
cd theme-engine/ThemeProxy && dotnet run -- --urls=http://localhost:5000
```

Or publish as a single-file executable (e.g. for shipping with NovaLog):

```bash
cd theme-engine/ThemeProxy
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# Output: bin/Release/net8.0/win-x64/publish/ThemeProxy.exe
```

Run the published exe: `ThemeProxy.exe --urls=http://localhost:5000`.

**Client with .NET proxy:** set `VITE_PROXY_URL=http://localhost:5000` (or configure the Vite proxy to target port 5000), then:

```bash
cd theme-engine/client && npm install && npm run dev
```

Open http://localhost:5173. Use “Fetch themes” with publisher/extension/version (e.g. GitHub, github-vscode-theme, 6.3.0), pick a variant, then edit tokens in the Live editor. Export saves the final merged theme as JSON.

### NovaLog sidecar (Windows)

On Windows, the NovaLog Avalonia app starts the .NET theme proxy automatically via `ThemeProxyManager`: it launches `ThemeProxy.exe` from the app’s base directory (or a ThemeProxy subfolder), binds it to a Windows Job Object so the proxy exits when NovaLog exits, and stops it on app shutdown. Place the published `ThemeProxy.exe` next to the NovaLog executable (or in a `ThemeProxy` subfolder, if you configure that path). No separate proxy process is needed when using NovaLog on Windows.

## Expansion

- **More tokens:** Add entries to the dictionary in `client/src/themeEngine.ts` (`VSCODE_TO_APP_TOKEN_MAP`).
- **Proxy errors:** Node: see `server/proxy.js` for 400/404/422/502 handling. .NET: see `ThemeProxy/Program.cs` for the same status codes and error handling.
- **Fallbacks:** Add or adjust `:root { --default-* }` and usage in `client/src/index.css`.
