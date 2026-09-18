# Verification — 2026-09-18

## Automated and protocol checks

- 510 unit tests passed, including strict material edit input validation.
- Revit 2024 add-in Release build: zero warnings, zero errors.
- Self-contained Windows x64 server started without a separately installed runtime.
- Real stdio MCP initialize and tools/list passed: identity `chinh-thang-revit-mcp`, version 1.1.0, 232 tools.
- Embedded native BIM policy resource read successfully through MCP.
- Installer preview, repeated installation and preservation of unrelated nested host configuration passed.
- Installed Codex TOML parsed successfully; pre-existing server entry retained; backup created.
- GitHub Actions build/test/package/stdio verification passed for the initial fork commit.

## Revit runtime checks

- Live `revit_get_current_view_info` returned an existing 3D view, scale 100.
- A separate scratch Revit document was used for material writes. The user's active project was not edited by those tests.
- Generic Appearance Asset: glossiness 0.63, direct reflectivity 0.21, highlights flag and bitmap assignment read back successfully.
- Bitmap physical dimensions read back as 600 x 300 mm.
- Dry-run rolled back both the material link and duplicated asset; asset count unchanged.
- Unknown schema property failed; rollback left no duplicate asset or changed material link.
- Saved/reopened scratch RVT retained appearance data.
- Advanced `PrismLayeredSchema`: roughness 0.47 and anisotropy 0.12 read back correctly.
- Bitmap connections for surface albedo, roughness, normal and cutout were accepted and read back as UnifiedBitmap assets.

## Boundaries

These checks establish build, protocol, Revit connectivity and the tested API operations. They do not certify visual render quality, every material channel/schema, every renderer or every Revit version. The generic asset editor rejects unsupported property types; it does not silently convert arbitrary PBR packages.

Editable Family requirements are embedded guidance. A comprehensive Family Editor authoring suite and automatic material-library/image-generation pipeline are not implemented. The existing tools plus checked Revit API code are available for those workflows.

During initial diagnosis, a call while Revit was still on Home correctly reported `No document is open`; this produced a failure toast. With a model open and MCP ON, the live call passed. The build was then adjusted to prefer the installed Revit API; CI falls back to the Revit 2024.0 package.

## Rebar and steel upgrade 1.1

- Three new tools compile against the installed Revit 2024 API: bent rebar paths, project detailing catalog and native detailed steel connection creation.
- Twenty-one new tests cover anchored/stair/spatial-chair paths, invalid geometry and ambiguous/excessive distributions.
- The 1.1 package is installed locally and its real stdio handshake exposes 232 tools.
- Live rebar scenarios are provided in `scripts/verify-rebar-live.cs`: a separate concrete fixture, dry-run, four bent-bar cases, native edits, invalid-bend rollback and save/reopen.
- Live Revit 2024 scratch tests passed: two-ended anchored path (6 bars), stair zigzag with maximum spacing (6 bars), planar chair (1 bar) spatial FreeForm chairs (3 bars), and top-slab bars with two downward ends (6 bars). All are native Rebar with actual bend arcs and the requested host/type.
- Each case passed a committed-then-rolled-back dry-run with no residual bars. An impossible short bend was rejected with clean rollback.
- Shape-driven layout was edited from 6 to 8 bars; spatial-chair geometry was edited using SetCurves. Saved/reopened RVT retained 5 sets totaling 24 bars.
- These geometry tests used a wide concrete wall fixture; they do not certify anchorage/cover across an actual slab-beam joint or a full stair assembly.
- Actual MCP calls passed catalog inspection and a bent-rebar dry-run. Invalid detailed steel connection type was rejected without fallback. Live current-view read and all 232 tools passed on the installed 1.1 server.
- GitHub Actions build/test/package/stdio checks passed for the detailing implementation commit ee6e3d5.
- Detailed steel connection fabrication geometry and capacity are not verified by creating a native handler. Loaded connection types and the Autodesk service are required.


## Version 1.2 — 2026-09-18, architecture / MEP / HSE

- 545 unit tests passed (including calculation/layout and HSE record validation). Revit 2024 API 24.0 Release build: 0 warnings / 0 errors.
- Installed local server identity 1.2.0, 244 tools; real stdio resource read and live current-view read passed.
- `scripts/verify-discipline-live.cs` creates only a separate scratch document. Live checks passed: two floor boundary rings / eight edges for a floor with one opening, unplaced room detection, audit truncation, a pipe with two open physical ends and 1% geometric slope, refusal of resolution without evidence, and HSE register save/reopen with one retained revision.
- Actual typed MCP calls passed for all three discipline audits and HSE register listing. The fixture is intentionally small; this does not establish completeness on linked, phased or large production models, family flex, code compliance, HVAC/hydraulic/electrical design or site safety.
- Version 1.2 rebar calculation tests passed for the explicitly bounded TCVN5574:2018 and EN1992-1-1:2004 profiles. `scripts/verify-designed-rebar-live.cs` passed on the installed DLL: net slab area 29 m² excluding one opening, 8 planar chairs for the fixture input 4 m²/chair, clean dry-run, repeat-region replacement without duplicates, failed replacement restoring all original supports, two downward anchors with actual rounded path length 4070 mm (3000 mm body + 2 × 535 mm), independent start-anchor toggle, 640 mm straight overlap after the rounded crank, and save/reopen of 13 Rebar sets. These fixture dimensions are not project design defaults. Positive native coupler creation and smart I-node connection creation remain unverified and experimental. Spatial/ZigzagRail density layouts also need separate runtime cases; the verified region case uses PlanarChair. Steel connection capacity and actual plate centering are not certified.
