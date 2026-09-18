# Verification — 2026-09-18

## Automated and protocol checks

- 487 unit tests passed, including strict material edit input validation.
- Revit 2024 add-in Release build: zero warnings, zero errors.
- Self-contained Windows x64 server started without a separately installed runtime.
- Real stdio MCP initialize and tools/list passed: identity `chinh-thang-revit-mcp`, version 1.0.0, 229 tools.
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
