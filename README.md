# Nyautomator StreamElements Module

StreamElements integration module for Nyautomator. It owns StreamElements OAuth and legacy JWT auth, the realtime event socket, REST helpers for loyalty/alerts/overlay replacements/bot announcements, automation nodes, settings and dashboard cards, MCP tools, and packaging.

## Source Layout

- `src/Nyautomator.StreamElements`: OAuth client, token refresh, and realtime Socket.IO client.
- `src/Nyautomator.Automation.StreamElements`: StreamElements event action, reactions, loyalty points value node, REST client, and event source registration.
- `src/Nyautomator.Module.StreamElements`: Module entrypoint, credential resolution, realtime runtime participant, and `/modules/streamelements/api/*` handler.
- `tests/Nyautomator.Module.StreamElements.Tests`: API handler and credential tests.
- `module/module.json`: Nyautomator module contribution manifest.
- `module/assets`: icon, settings/dashboard/tab UI, config schema, and graph template assets.

## Build

From inside a Nyautomator checkout (`Submodules/Nyautomator.StreamElements`), the projects reference the host SDK projects by source:

```powershell
dotnet build .\Nyautomator.StreamElements.slnx
dotnet test .\Nyautomator.StreamElements.slnx
```

Standalone binary-reference build against host SDK DLLs:

```powershell
dotnet build .\Nyautomator.StreamElements.slnx -p:UseNyautomatorProjectReferences=false -p:NyautomatorReferencePath=C:\Path\To\NyautomatorDlls\
```

## Packaging

```powershell
.\scripts\pack-module.ps1 -Configuration Release -UseNyautomatorProjectReferences false -NyautomatorReferencePath C:\Path\To\NyautomatorDlls\
```

The script writes `artifacts/modules/streamelements-<version>.zip`. Install it from the Nyautomator **Modules** tab. The package contains only StreamElements-owned assemblies, required third-party dependencies, `module.json`, and `module/assets/**`; host SDK assemblies stay compile-time references and are excluded.
