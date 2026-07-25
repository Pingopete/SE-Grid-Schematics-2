# Architecture

From block data to lit pixels, and why each stage is the way it is. Most of the
"why" here was paid for with in-game measurement; the notes exist so the same
wrong turns don't get taken twice.

## The goal

Draw the ship from its true geometry so the image is **resolution independent** —
sharp, accurate edges at any zoom, with smooth shading between them. No baked
textures whose pixels run out when you zoom in, and no separate "zoomed in" and
"zoomed out" renderers that look different from each other.

## Pipeline

### 1. Scan (background thread, ~30 ms per pass for 15 ships)

`OccupancyScan` walks every block on a grid via DCS and accumulates three
orthographic projections (top / side / front).

- Blocks that fill their bounding box stamp their cells directly.
- Blocks that don't are handed to `BlockShapes`, which **recovers the block's
  analytic solid from its own voxel data** (see below).
- Recovered blocks stamp **fractional** coverage at 1/16 cell. This is what makes
  a ramp a real diagonal in the data instead of a staircase.
- Blocks with no analytic solid fall back to their own cell boxes — which are
  solid cells, and must therefore write the coverage field just like any other
  solid box. (Omitting that made every such block invisible, since the display
  field is `tone × coverage`.)

Output per view: an integer thickness field and a byte coverage field (0–16).

#### Recovering block geometry

The engine voxelizes every block into 25 cm cells — a 2.5 m block is 10×10×10.
A flat face cutting through that grid leaves a staircase, but the staircase still
encodes the exact plane that produced it. So rather than guessing which canonical
solid a block resembles, the solid is recovered directly as an intersection of
half-spaces:

1. For each candidate direction (integer normals, gcd-reduced), take the
   **tightest plane that still contains every occupied cell** — its supporting
   plane. Its offset is placed midway between the outermost occupied cell centre
   and the first empty cell beyond, because that is where the true surface must lie.
2. Prefer the **simplest explanation**: if one plane accounts for every empty
   cell, that is the block's face. Several plane sets can reproduce identical
   voxel data while implying different surfaces, and adding planes carves detail
   the block doesn't have.
3. Otherwise cover the empty cells with as few planes as possible, counting only
   cells a plane genuinely *excludes*.
4. Directions up to ±2 are tried first (faces, 45s, 1:2 long slopes, corner
   diagonals), escalating to ±4 when that fails or needs more planes than a
   better-angled single cut would.

A block is convex exactly when the recovered hull leaves no empty cell
unaccounted for. Trusses, handrails, stairs and drills fail that test and keep
their cell boxes, which for those shapes is the honest answer.

Two subtleties matter:

- **Quantisation ambiguity.** A cell whose centre lies within a fraction of a
  cell of the surface is a coin flip — the engine's own voxelizer had to round it
  one way. Those cells are tolerated when judging a single-plane fit. The
  tolerance must *not* be applied per-plane during multi-plane selection, or
  several planes each claim cells none of them removes, and a staircase passes as
  a convex solid.
- **Precision limit.** Cell data localises a surface to about half a cell, so
  multi-plane solids carry a few percent sub-cell coverage error. Single-plane
  shapes — most of the armour set — come back exact.

Verified against voxelized ground truth in `scratchpad/ShapeFit`: 1:1, 1:2, 2:1
and 3:4 slopes, corners, tips, inverted corners and half slabs all recover with
0.00% coverage error; trusses and staircases are correctly rejected.

Scans are reused. `ContentEquals` compares extents, counts and all three
projections; if nothing changed, the previous scan object is kept and every cache
hanging off it survives. Band geometry is only rebuilt when the ship is actually
edited, not on the 2-second scan cadence.

### 2. Tone field (once per view + mode)

`ToneFields` / `ToneMaps` map the raw data to display brightness:

- **Thickness** — total material along the view ray, sqrt ramp, min-max normalised
- **Complexity (x-ray)** — counts solid↔void transitions, so structure layers read
- **Voids** — enclosed empty space made bright over a dim hull ghost

Min-max normalisation matters: an earlier version mapped the *most common* value to
near-invisible, which read as "missing sections of the ship".

### 3. Band geometry (background thread, ~50–200 ms, cached)

`ToneBands` converts the tone field into ~39 nested iso-contour polygons — like
elevation lines on a map. This is the resolution-independent object: pure geometry
in cell space, built once, drawn at every zoom.

- Contours come from marching squares **with interpolation** (`Contour`). Because
  edge cells carry fractional coverage, the interpolated contour reconstructs the
  true analytic edge, not the cell boundary.
- **Corner sharpening.** Marching squares cannot express a corner — it draws one
  chord per cell, so every sharp 90° corner comes out chamfered by half a cell.
  The field's gradient can tell us: where the boundary directions at a chord's two
  ends diverge, the true silhouette has a vertex at the intersection of the two
  boundary lines. Insert it. Where the directions agree it's a genuine slope, so
  ramps are left exactly alone.
- Loops are oriented by signed area plus an inside test, so nonzero-winding fills
  punch holes correctly.
- **LOD ladder.** Each loop is simplified (Douglas-Peucker) at fixed cell-space
  error bounds (0.05 / 0.15 / 0.5 cells). The renderer picks the coarsest tier whose
  error stays under ~1/3 of an on-screen pixel — invisible by construction, and the
  same rule at every zoom.

A silhouette-only band set is published first (~5 ms) so a panel paints immediately
instead of booting blank.

### 4. Draw (per frame, only when something changed)

`VectorLcd.DrawBands` transforms the cached cell-space loops into the window with a
single scale+offset and submits one `DrawFill` per band, back to front. Zoom and pan
are *only* that transform — the geometry, the brightness maths and the draw path are
identical at every zoom level.

- Loops outside the window are rejected by bounding box; loops crossing it are
  **clipped** (Sutherland-Hodgman, in cell space) so cost tracks what's on screen.
  The scissor rectangle clips *pixels*, not path submission, so without this a
  contour spanning the whole ship still submitted thousands of off-screen vertices
  every frame.
- Repaints are event-driven: cursor moved, view changed, geometry changed. Every
  forced rebuild clears and re-records the panel's render target.

Measured cost: ~0.25 ms per record, well under 1% of a core, holding 60 fps.

## Rendering decisions that were tested and reverted

**Bands must be drawn stacked, not as rings.** Drawing each band as a disjoint ring
(its contour plus the next one's as a hole, at its absolute tone) looks like an
obvious win — each pixel painted once instead of up to 39 times. It is wrong twice
over:

- *Visually*: at a shared boundary the two neighbouring strips each cover only part
  of the pixel, so it composites to `a(1 − a·f(1−f))` — around 12% **darker than
  both**. A dark line at all 39 boundaries reads as cel-shaded contour banding.
  Stacked layers have no such dip, because the bands underneath fully cover the pixel.
- *On performance*: rings need every contour twice (once as an outline, once as a
  hole) and turn each fill into a complex multi-loop path. Measured 44k → 74k segments
  and 0.5 → 1.4 ms per record, with a visible in-game frame rate drop that disappeared
  on revert.

The lesson generalises: on this canvas the cost driver is **path complexity and
segment count, not pixel overdraw**. Don't "optimise" overdraw here without measuring
path cost first.

**Any forced-repaint condition needs a staleness bound.** A pending texture that could
never finish loading (its pipeline had been replaced) pinned a panel at 60 full
rebuilds per second indefinitely. The tell in the log was `pendAge` climbing past
300,000 ms.

## Engine constraints worth knowing

- `IDrawBatch` fills take a flat colour or a **two-colour** gradient. There is no
  path-based clipping — `ScissorPush` is rectangles only. `DrawImage` has an unused
  `maskTexture` parameter, which is the only masking primitive available.
- Textures are file-backed only, and the loader accepts `.png` / `.dds` / `.jpg` /
  `.slug`. A bad file throws inside the render thread's replay where it cannot be
  caught — that crashes the game, so generated images must be validated offline first.
- The texture streamer evicts by distance and priority, and drawing an evicted
  texture silently draws *nothing*. Pinning via `UISystem.PreloadTexture` is required.
- LCD content repaints are event-driven. Setting `ContentDirty` from inside a render
  postfix does nothing (it's cleared after `Render` returns); driving 60 fps panels
  means re-invoking `RebuildSurfaceContent` from the per-frame tick hook.
- Grid entities are re-created mid-session, so entity hashes drift. Ship keys are
  derived from tagged panel block positions instead.

## Telemetry

The log carries two lines that make performance work tractable:

- `BandCost` — records/s × ms = % of a core, plus segment count, fill calls and LOD
  tier. This is what proved our own CPU cost was under 1% and the expense was
  engine-side.
- `LcdTick` — an FPS proxy. The tick hook fires once per LCD render component per
  frame, so frames = ticks / distinct components / seconds.
