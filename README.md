# Grid Schematics 2

Ship schematic displays for **Space Engineers 2**, rendered live onto in-world LCD panels.

The SE1 mod ([SE-Grid-Schematics](https://github.com/Pingopete/SE-Grid-Schematics)) reconstructs
ship geometry with raycasts. SE2 exposes block data directly, so this version reads the real
grid and draws it as resolution-independent vector geometry — the picture is identical in
character at every zoom level, with slopes and edges reconstructed from sub-cell coverage
rather than drawn as 25 cm stair-steps.

Status: **working proof of concept** against SE2 alpha (build 24225481). Not a released mod —
SE2 has no public mod API yet, so this loads through the engine's own plugin loader.

## What works

- Occupancy scan of every grid in the scene (~30 ms per pass for 15 ships, no raycasts)
- Sub-cell shape fitting: ramps, wedges and corners are fitted to analytic solids and stamped
  at 1/16-cell resolution, so their true diagonals survive into the render
- Three depth modes: material thickness, structural complexity (x-ray), interior voids
- Live vector rendering into the panel's own render target at 60 fps
- Aim-driven cursor with persistent per-panel calibration, on-panel buttons, zoom and pan
- Top / side / front views

## Layout

| Path | What it is |
|---|---|
| `src/GridProbe` | Bootstrap plugin. Loaded once at game start; applies Harmony patches and hosts the logic assembly in a collectible load context. |
| `src/GridProbe.Logic` | All the actual logic. Hot-reloads into a running game in ~2 s. |
| `tools/ApiInspector` | Offline reflection/IL dumper for exploring engine assemblies. |
| `docs/` | Architecture notes and captured engine API details. |
| `scripts/` | Build and launch helpers. |
| `output/` | Runtime log and image dumps (git-ignored). |

Assembly names (`GridProbe*`) are historical, from the probe phase that preceded this repo.
They are load-bearing at runtime — the bootstrap looks for `GridProbe.Logic.dll` by name —
so renaming is a deliberate follow-up, not a cosmetic edit.

## Build

Needs the .NET 9 SDK and a local SE2 install. Paths live in `Directory.Build.props`:

| Property | Default | Meaning |
|---|---|---|
| `SE2Dir` | `D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2` | Engine assemblies to reference |
| `DeployDir` | `D:\SE2Probe` | Where built DLLs are copied; the bootstrap watches this |

```
scripts\build.bat
```

Override without editing the file:

```
dotnet build src\GridProbe.Logic\GridProbe.Logic.csproj -c Release /p:SE2Dir=... /p:DeployDir=...
```

## Run

1. `scripts\build.bat`
2. Steam running, then `scripts\launch-se2.bat`
3. Load a world with a ship, place an LCD panel, and put `[GS]` in its text or name
4. The panel takes over and draws that ship

Rebuilding `GridProbe.Logic` while the game runs hot-reloads it — no restart. Changing the
bootstrap needs a restart.

### Controls

| Input | Action |
|---|---|
| Aim at panel | Moves the cursor |
| Left click | Press the on-panel buttons (zoom, view, mode, calibrate, refresh) |
| Drag | Pan |
| Ctrl+Shift+Alt + Left click | Force-start cursor calibration |
| Ctrl+Shift+Alt + Middle click | Log every block in the hovered column |

Cursor calibration is a 6-click sequence (3 targets from two standpoints) — depth is not
observable from a single position. Results are stored per panel, relative to the block, and
survive world reloads.

## Docs

- [`docs/architecture.md`](docs/architecture.md) — the pipeline from block data to lit pixels,
  and the measured reasons behind the rendering decisions
- [`docs/se2-api-notes.md`](docs/se2-api-notes.md) — engine API surface discovered by inspection
- [`docs/pluginhost-il.md`](docs/pluginhost-il.md) — IL of the engine's plugin loader
