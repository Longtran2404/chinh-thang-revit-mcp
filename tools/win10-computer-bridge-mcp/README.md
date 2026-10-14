# Win10 Computer Bridge MCP v0.3 (preview)

Independent, consent-gated GUI MCP bridge for **Windows 10 22H2** and Codex. Does not patch `codex-computer-use.exe` and does not replace integrated Computer Use in ChatGPT Work.

## Two separate modes

1. **Background capture:** `PrintWindow` captures an allowlisted non-minimized window without moving the physical cursor or focusing the app. Some CAD/DirectX viewports may be blank. Never fall back silently to a screenshot of an occluded desktop.
2. **Foreground mouse:** Full physical mouse support via Win32 `SendInput`: left/right/middle/X1/X2, single/double click, drag, timed hold, horizontal/vertical wheel, Ctrl/Shift/Alt/Space modifiers. This mode **occupies your cursor and keyboard focus** and is only enabled manually with Windows consent **for every action**. On the same Windows desktop, true concurrent background mouse control of arbitrary CAD apps is not reliable.

For genuine concurrent GUI work, use a dedicated Windows VM or workstation. For Revit modeling, prefer the existing Revit API MCP tools in this repository over GUI automation.

## Safety contract

- Default `mouse_control_mode=disabled`, `allow_processes=["notepad.exe"]`.
- New `capture_id` valid for 90 seconds; reject moved/resized windows or reused screenshot tokens.
- `dry_run=True` unless explicitly disabled, and physical Windows Yes/No confirmation is mandatory.
- Finite drag/hold (max 6 seconds, max 160 steps), F12 abort while dragging, guaranteed best-effort key/button release.
- No network listener, registry edits, administrative privilege requests, OpenAI binary modifications, or arbitrary executable launching. **Image results are transmitted through the MCP client to the connected AI service**.
- Validate first on an empty Notepad window, not production CAD/Revit models.

## Install on Windows 10

Requires Python **3.11** from python.org, network for `pip install`, Codex CLI installed. Extract the source and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install_windows.ps1
$py = "$env:LOCALAPPDATA\Win10ComputerBridgeMCP\venv\Scripts\python.exe"
& $py .\scripts\smoke_test.py
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install_windows.ps1 -InstallCodex
codex mcp list
```

Config is `%USERPROFILE%\Win10_ComputerBridge\bridge_config.json`. To test mouse after preview and verifying output, change `mouse_control_mode` to `foreground_confirm_each`. Every real action additionally opens a Windows approval dialog.

## MCP tools

`bridge_status`, `inspect_windows`, `capture_window`, `mouse_action`, `emergency_release`.

## Verification scope

Win10 Bridge v0.1 Notepad `Inspect` and `Click` were tested manually; v0.2 `PrintWindow` captured an image with a full Notepad window (black DWM border can appear). v0.3 policy unit tests pass in a non-Windows environment. **v0.3 MCP protocol/Win32 interaction has not yet been tested on Windows**. A draft PR remains unmerged until owner tests screenshot, all mouse gestures, DPI/multi-monitor and emergency release.

License Apache-2.0; contains no OpenAI/Autodesk binaries.
