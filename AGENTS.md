<!-- Modified for Chinh Thang Revit MCP, 2026-09-18. See FORK_CHANGES.md. -->
# Chinh Thang Revit MCP developer guidance

This is an independent source-build distribution based on bimwright/rvt-mcp.
244 Revit tools with `--toolsets all` (247 with adaptive bake).

- Read LOCAL_SETUP.md and FORK_CHANGES.md before installation changes.
- Build with scripts/build-local.ps1, which runs tests and disables automatic add-in deployment.
- Install this fork using scripts/install-local.ps1; preview with -WhatIf first.
- Preserve existing host configuration and backups. Only manage this fork's MCP entry and manifest.
- Do not install an upstream release over this fork or load both plugins in one Revit process.
- Keep LICENSE, NOTICE and applicable upstream attribution in distributions.
- No credentials, user config, Revit model files or Autodesk API binaries in Git.
- Maintain transaction/undo behavior and authentication. Never bypass the Revit UI thread.
- Validate using unit tests, the real stdio handshake and a read-only live Revit call.
- Distinguish built, installed, protocol-tested and live-Revit-tested status. Never claim live success from a build alone.
- Existing upstream scripts are retained for reference; the supported fork workflow is build-local/install-local.

Read docs/native-modeling-policy.md before any furniture, door, wall, detail or material task. Native parametric editability is mandatory for requested furniture families.
