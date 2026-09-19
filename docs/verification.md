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

## Checked steel details — source update 2026-09-19

- 562 tests passed, zero failures or skips. Revit 2024 Release plugin build: zero warnings and errors. The source exposes 245 tools (248 with adaptive bake).
- Live Revit testing loaded the newly built handler assembly into the separate workshop demonstration, rather than replacing the installed add-in. It created 32 editable straight anchor shanks, diameter 24 mm and length 610 mm, fully contained in eight concrete footings. These dimensions are demonstration inputs, not calculated anchorage requirements.
- A valid anchor followed by one protruding through a footing edge was rejected by the actual solid-containment check. The entire batch rolled back; the family-instance count remained 705. A separate valid dry-run also rolled back cleanly. An earlier malformed-position test only checked input rejection and is not evidence of geometric containment.
- TCVN 5575:2024 bolt-layout checks are numeric unit-tested checks within the documented Table 43 scope. They do not certify actual generated connection geometry or structural capacity. Positive end-to-end native smart-node creation with stiffeners remains unverified.
- The local 3-by-6 m longitudinal bay / 18 m span workshop is a geometry demonstration. Purlin family section geometry, cleat alignment, knee fit, welds, brace end fittings and cold-formed member design still require review. It is not a completed fabrication or construction package. Private RVT/RFA files, temporary authoring scripts and copyrighted standards are not published.
- This is a source update under Unreleased. The installed add-in is still the previously verified 1.2.0 / 244-tool build; publishing source does not install the new handler in Revit.
- Final packaged-server stdio check passed initialization, 245-tool discovery and the native-modeling guidance resource. The final read-only current-view call timed out after 45 seconds; live connectivity was not reconfirmed at publication time. Earlier direct handler geometry tests above remain separate evidence.

## Version 1.2.1 — rebar continuity safeguards, 2026-09-19

- 569 unit tests passed; seven new geometric regression cases cover the observed 158 mm column gap, partial overlap, reversed direction, separated lap, perpendicular crossings, touching ends and degenerate segments.
- A separately loaded newly built Revit 2024 assembly passed native scratch checks: exact duplicate rollback, 500 mm partial-overlap rollback, separated parallel lap accepted, valid two-downward-anchor dry-run, missing receiver rejected, anchorage outside concrete rejected, an anchor crossing an actual floor opening rejected, out-of-concrete second distribution position rejected, and silently disabled anchorage rejected. Rejected calls preserved the pre-call bar count.
- Read-only use of the new audit on 12 selected existing column sets reproduced 24 gap candidates. The result was REVIEW_REQUIRED and construction_ready=false. No production rebar was changed by these tests.
- Coverage is bounded: whole-path duplicate and parallel straight-body penetration checks; coaxial end-gap candidates; enabled anchor centreline embedment in explicitly supplied receiving concrete. This does not certify bar-radius containment, cover, joints, stagger, couplers, nonparallel/curved-body clashes, required lap length or capacity. The audit cannot prove missing anchors from geometry alone.
- The implementation was tested by loading the built assembly; this is separate from installing/registering the new typed tools. At packaging time the previous add-in was still loaded by Revit. Installation requires Revit to be closed. See the current task handover for the eventual installation result.
- Final 1.2.1 package: plugin build zero warnings/errors; real stdio identity 1.2.1 with 246 tools; native guidance resource and read-only current-view call passed. A further native test confirmed that failed managed replacement restores the original bar and ID (3 sets before and after). All scratch checks were rerun against the final compiled handler assembly and passed.
