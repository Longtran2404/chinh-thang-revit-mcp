# Chinh Thang Revit MCP — changes from upstream

Baseline: `bimwright/rvt-mcp` commit `5c127f80a6bb443555ce6a6a3c5cb4b364678436`.
Modified by Minh Long Tran's fork on 2026-09-18, distributed under Apache-2.0.

- Separate product identity, repository, MCP server identity, NuGet package identity and Revit ribbon label.
- Local Revit 2024 source build, portable self-contained server, scoped installer and stdio smoke test.
- Removed vendor-specific client setup, associated agent guide and benchmark template.
- Optional code condensation and suggestion naming now use explicitly supplied provider interfaces; no vendor API key requirement. No cloud provider is configured or called by this fork.
- Updated regression tests to verify provider callbacks without environment credentials.
- Preserved the Revit API commands, transactions, token authentication, localhost transport and tool names.
- Retained upstream C# namespaces and local RvtMcp runtime data format for compatibility. Do not run the upstream plugin and this plugin together in the same Revit instance.
- Removed upstream registry publication manifests. This fork is built and distributed from its own repository.

The Git history retains upstream authorship. Source files with edits carry change notices. Deleted files are recorded in the fork commit.

- Extended material appearance tools with actual render schema introspection, typed property/bitmap edits, physical texture transforms, private asset duplication and transactional dry-run. Added 14 input-validation cases.
- Fixed five upstream .NET Framework string-search incompatibilities in Revit 2024 sheet/titleblock handlers.
- Embedded native editable-family and full material-workflow guidance as an MCP resource.
- Updated golden tool-schema JSON snapshots for the four new optional material appearance arguments.

- Prefer the installed Revit 2024 API assemblies; CI fallback targets 2024.0, avoiding an unnecessary 24.2 dependency on base 2024 installations.

- Fixed completion notifications for C# scripts returning structured objects/arrays; added two regression tests.

- Version 1.1: added explicit native bent-rebar path authoring (planar and spatial), bounded distributions, real commit validation inside reversible dry-run groups, detailing type inspection and detailed steel connection creation without generic fallback.
- Added 20 geometry/layout validation cases and a reproducible scratch-model Revit test script. Tool surface is 232 standard / 235 adaptive.
- Documented FreeForm host-constraint limitations and the distinction between geometry generation and engineering design checks.

- Added an explicit top-slab inverted-U example with both anchorage legs downward, an additional input test, and live endpoint-elevation checks before/after save/reopen.
