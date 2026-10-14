"""Local stdio MCP server: capture in background, GUI mouse only with consent.

No binary modifications, no injection, no PostMessage background spoofing.
An MCP screenshot is sent to the configured AI client; treat it as shared data.
"""
from __future__ import annotations

import base64
import json
import os
from pathlib import Path
import threading

from mcp.server.fastmcp import FastMCP
from mcp.types import CallToolResult, ImageContent, TextContent

from .policy import CaptureRegistry, normalize_allowlist, validate_mouse

APP_NAME = "Win10 Computer Bridge (local)"
mcp = FastMCP(APP_NAME)
REGISTRY = CaptureRegistry()
MOUSE_MUTEX = threading.RLock()


def load_config() -> dict:
    cfgfile = Path(os.environ.get("WIN10_BRIDGE_CONFIG", str(Path.home() / "Win10_ComputerBridge" / "bridge_config.json")))
    cfg = {"allow_processes": ["notepad.exe"], "mouse_control_mode": "disabled"}
    if cfgfile.exists():
        raw = json.loads(cfgfile.read_text(encoding="utf-8-sig"))
        if not isinstance(raw, dict):
            raise ValueError("Configuration must be JSON object")
        if set(raw) - {"allow_processes", "mouse_control_mode", "notes"}:
            raise ValueError("Unexpected configuration keys")
        cfg.update(raw)
    cfg["allowed"] = normalize_allowlist(cfg["allow_processes"])
    if cfg["mouse_control_mode"] not in {"disabled", "foreground_confirm_each"}:
        raise ValueError("mouse_control_mode must be disabled or foreground_confirm_each")
    return cfg


def backend():
    # Delay Win32 import for pure-Python policy unit tests on other operating systems.
    from . import windows_native
    return windows_native


@mcp.tool()
def bridge_status() -> dict:
    """Read-only bridge capabilities and safety settings; no mouse movement."""
    cfg = load_config()
    return {
        "windows_capture": "PrintWindow (can fail for GPU CAD viewports)",
        "capture_mode": "background, no cursor movement, window must not be minimized",
        "mouse_mode": cfg["mouse_control_mode"],
        "allowed_processes": sorted(cfg["allowed"]),
        "full_mouse": ["move", "click", "double_click", "drag", "hold", "scroll vertical", "scroll horizontal", "left", "right", "middle", "x1", "x2", "ctrl", "shift", "alt", "space"],
        "concurrent_desktop_mouse": False,
        "emergency_stop": "Hold F12 during a drag; pressed buttons released in finally",
        "note": "For uninterrupted concurrent GUI work use a separate VM/workstation; Revit API MCP is preferred for BIM edits."
    }


@mcp.tool()
def inspect_windows() -> dict:
    """List only explicitly allowlisted, non-minimized desktop windows."""
    cfg = load_config()
    wins = backend().enumerate_windows(cfg["allowed"])
    return {"windows": [
        {"window_id": w.id, "title": w.title, "exe": w.exe,
         "pid": w.pid, "rect": {"x": w.rect.left, "y": w.rect.top,
                              "width": w.rect.width, "height": w.rect.height}}
        for w in wins]}


@mcp.tool(structured_output=False)
def capture_window(window_id: str) -> CallToolResult:
    """Capture an allowlisted window WITHOUT focusing it, return PNG plus capture_id.

    GPU viewports may be blank; verify image visually before using coordinates.
    MCP screenshot bytes are transferred to the AI client, not solely kept local.
    """
    cfg = load_config()
    win = backend().get_window(window_id, cfg["allowed"])
    png, method, blank = backend().capture_window(win)
    if blank:
        raise RuntimeError("PrintWindow returned a uniform/possibly blank image; NO click token issued")
    with MOUSE_MUTEX:
        t = REGISTRY.issue(win.id, win.rect, method)
    info = {"window_id": win.id, "capture_id": t.token,
            "width": win.rect.width, "height": win.rect.height,
            "method": method, "screenshot_verified_by_user": False,
            "note": "Coordinates are relative to OUTER window; may include borders. Screenshot may include sensitive content."}
    return CallToolResult(content=[
        TextContent(type="text", text=json.dumps(info, ensure_ascii=False)),
        ImageContent(type="image", mimeType="image/png", data=base64.b64encode(png).decode("ascii"))
    ])


def ask_local_consent(summary: str) -> bool:
    """Unspoofable via MCP args: operator must click Yes in a Windows dialog."""
    import ctypes as ct
    user32 = ct.WinDLL("user32", use_last_error=True)
    # YES/NO + warning icon + TOPMOST. UI intervention is mandatory for host mouse.
    result = user32.MessageBoxW(
        None,
        "MCP requested physical mouse control. Your mouse/focus will be occupied.\n\n"
        + summary + "\n\nAuthorize exactly this one finite operation?",
        "Win10 Bridge - local human confirmation",
        0x00000004 | 0x00000030 | 0x00040000
    )
    return result == 6  # IDYES


@mcp.tool()
def mouse_action(
    window_id: str,
    capture_id: str,
    action: str,
    x: int,
    y: int,
    button: str = "left",
    end_x: int = -1,
    end_y: int = -1,
    duration_ms: int = 400,
    steps: int = 16,
    wheel_ticks: int = 1,
    wheel_axis: str = "vertical",
    modifiers: str = "",
    dry_run: bool = True,
) -> dict:
    """Physical mouse control with fresh screenshot + native human consent.

    action: move|click|double_click|drag|hold|scroll. button: left|right|middle|x1|x2.
    Drag holds the button through intermediate positions; hold has a bounded duration.
    wheel_axis: vertical|horizontal. modifiers: ctrl+shift+alt+space subset.
    DEFAULT dry_run=True; turning it off never bypasses GUI confirmation.
    Disabled until owner edits local config to foreground_confirm_each.
    IMPORTANT: The host's real mouse and focus ARE shared while executing.
    """
    cfg = load_config()
    m = backend()
    mods = validate_mouse(action, button, duration_ms, steps, wheel_ticks, wheel_axis, modifiers)
    with MOUSE_MUTEX:
        win = m.get_window(window_id, cfg["allowed"])
        ex = x if end_x == -1 else end_x
        ey = y if end_y == -1 else end_y
        sx, sy = win.rect.absolute(x, y)
        tx, ty = win.rect.absolute(ex, ey)
        if action == "drag" and (ex, ey) == (x, y):
            raise ValueError("Drag must specify a different end point")
        REGISTRY.verify(capture_id, window_id, win.rect)
        summary = f"{win.exe} | {win.title[:80]}\n{action} {button} from {x},{y} to {ex},{ey} | {duration_ms} ms | wheel={wheel_ticks} {wheel_axis} | mods={mods}"
        if dry_run:
            return {"dry_run": True, "summary": summary,
                    "physical_coordinates": [[sx, sy], [tx, ty]],
                    "mouse_control_mode": cfg["mouse_control_mode"],
                    "note": "No input sent. Set dry_run=false after local owner enables mouse mode."}
        if cfg["mouse_control_mode"] != "foreground_confirm_each":
            raise PermissionError("Mouse disabled in local config; background capture still works")
        if not ask_local_consent(summary):
            return {"performed": False, "reason": "Owner declined local Windows consent"}
        refreshed = m.get_window(window_id, cfg["allowed"])
        REGISTRY.verify(capture_id, refreshed.id, refreshed.rect)
        REGISTRY.consume(capture_id)  # one use only; re-capture before next action
        m.apply_mouse(refreshed, action, button, x, y, ex, ey,
                      duration_ms, steps, wheel_ticks, wheel_axis, mods)
        return {"performed": True, "mouse_action": action,
                "notice": "Sent physical input; software result unverified. Capture again to verify."}


@mcp.tool()
def emergency_release() -> dict:
    """Best-effort release of any injected mouse buttons and modifiers; no dialog."""
    backend().release_all()
    return {"released": True, "note": "Best-effort; hold F12 during drags as emergency interrupt"}


def main() -> None:
    # Read config at startup so malformed settings fail fast.
    load_config()
    mcp.run(transport="stdio")


if __name__ == "__main__":
    main()
