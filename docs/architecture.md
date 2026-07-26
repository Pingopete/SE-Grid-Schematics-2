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

#### Thickness is volume, coverage is area

These are different quantities and must be accumulated differently.

**Thickness** sums each cell's volume fraction along the view ray — how much
material a ray passes through.

**Coverage** is the fraction of a cell's *projected* area that any material
covers. It is built by unioning 4×4 projected sub-masks, which each block's
stamp carries per cell (`Stamp.MaskXY/MaskYZ/MaskXZ`).

Using volume for coverage — taking a max of per-cell fill along the ray — is
wrong twice over: a cell sliced corner-to-corner by a slope is half full by
volume yet completely opaque when viewed along the slice, and two blocks can
cover complementary halves of one cell. Both under-count, which feathers what
should be a one-cell edge transition into a multi-cell ramp
(`48, 64, 80, 128, 160, 192, 208, 255` across a single 45° edge). The contour
then wanders inside that ramp, cutting small notches into diagonal members.
Unioning projected sub-masks gives `0, 0, 255, 255` — a sharp edge.

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

- **Thickness** — total material along the view ray, sqrt ramp
- **Complexity (x-ray)** — structural layers crossed, so interior structure reads
- **Voids** — enclosed empty space emphasised over the structure containing it

All three read **continuous quantities measured from the recovered geometry**, so
their gradients come from the shape itself.

Thickness was always continuous — material length along a ray varies smoothly
because the geometry does. Layers and voids originally counted *whole cells*,
which posterizes: each count owns a slab of the tone range and leaves unused gaps
between. Measured on a real ship, complexity occupied 7 tone bins with *empty*
ranges between them, against thickness's 13 evenly spread.

The fix is to measure them the same way thickness is measured. `DepthChannels`
now works on **sub-cell depth spans**: for a block whose analytic solid was
recovered, the first and last occupied cell in a column are only partly filled,
and that fraction says where the surface actually sits. A slope therefore
produces spans that slide smoothly from column to column.

- **Layers** weights each gap by how open it is (`1 − e^(−gap/1.5)`) rather than
  counting it. A hairline seam between two plates is not a whole extra
  structural layer, and the value rises smoothly as a gap widens instead of
  stepping the moment it appears.
- **Voids** is enclosed empty length between first and last material — continuous
  for the same reason.

#### Setting the display range

Every mode ends in the same step, `ToneMaps.Ramp`, which fits the tone range to
that mode's own measured field. Nothing downstream rescales — `ToneBands` places
its iso-levels across whatever range arrives and renders each band at its
absolute tone — so whatever range this step produces *is* what the panel shows.

The ends come from **percentiles, not the outright min and max**. Thickness used
raw min/max and the endpoints were set by freak columns at both extremes:
measured on a real ship, the top 0.5% of columns (a few deep shafts) owned 21% of
the range, so only 1% of the image reached the brightest eighth of the scale and
none of it reached white. At 1%/99% the ends *are* the true min and max for any
ship whose extremes are more than a handful of columns wide; anything outside
them clamps, so nothing is lost — it just is not allowed to set the scale alone.

Voids blends rather than sums. The hull is a dim ghost over `Floor..120`; the
void reading then carries the column the rest of the way to white, so the
emptiest column reaches 255 whatever its hull thickness is. Adding two ramps (the
previous version) required a column to sit at the thickness maximum *and* the
void maximum at once to reach white, which almost never happens — the mode topped
out near 240 with its brightest quarter unused. The blend keeps the ramp
continuous: at zero void it sits exactly on the hull tone.

An earlier version was worse still, confining the hull to 35–75 while voids got
110–255, which put 81.6% of occupied pixels inside a 40-level band.

A degenerate field (a flat plate, every column identical) reports `hi == lo` and
lights the whole field at full tone. A fudged epsilon range would instead push it
all to the dark floor.

Note what is deliberately *not* done here: a blur over the cell grid produces
gradients too, but they are a smear of coarse data rather than a property of the
shape. Blurring was tried and removed — the values must be continuous at source.

Note that the **band count is the output bit depth** — within a band the alpha is
constant, so it caps how many greys the panel can show however smooth the field
is. Cost scales linearly with it.

#### Panel response correction

The LCD does not show back what you write. It is an emissive surface running
through the game's HDR path, and its response is heavily lifted: a linear alpha
ramp reaches white within a couple of steps out of seventeen. Tone written
straight through therefore lands almost the whole ship in the flat top of that
curve, which is why renders came back pale and washed however well the tone
field itself was fitted.

`ToneBands.PanelCurve` corrects for it at the single point where tone becomes
alpha. This is a **display calibration and it belongs at the boundary**, not in
the tone maps — folding it into them would change what the three modes mean.

- `PanelGamma` (2.8) — the correction itself.
- `Knee` (0.2) — below this the curve goes linear. A power curve steep enough to
  undo the lift squeezes the bottom of the range into a few percent of the alpha
  range, where 8-bit alpha cannot hold the steps apart and the dark shades
  collapse into each other. sRGB carries a linear toe for the same reason.
- `MinAlpha` (1/255) — floor so the thinnest column cannot vanish entirely and
  take the silhouette's edge with it. One step, not more: at 4/255 the darkest
  tone composited above the panel's black threshold and the render could never
  reach black at all.
- `VectorLcd.BlitBrightness` (128) — the panel's white point, i.e. the top of the
  range it can still distinguish. Alpha's 255 steps are spread across it. Too
  high and they pile up past saturation looking identical; too low and the top
  never reaches white. **It must move together with `PanelGamma`**: it sets where
  the top lands, and raising it multiplies every tone below it by the same
  factor, so gamma is what holds the mids down.

`VectorLcd.DrawToneRamp` is the instrument, off by default. It draws a linear
alpha control row, the raw colour response, and fine sweeps of the toe and
shoulder. Two cautions learned the hard way:

- **Measure through alpha, not colour.** Rows that vary colour at full alpha
  measure the panel but not the path the ship is drawn through. Correcting from
  those alone means correcting a curve you never measured.
- **The panel blooms.** A solid-white row adjacent to the row being read bleeds
  into it and lifts its dark end. Two patches that composite to the same value
  then read differently, which invalidates everything inferred from them. Rows
  are separated by black gaps and the white shoulder row is kept last.

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
