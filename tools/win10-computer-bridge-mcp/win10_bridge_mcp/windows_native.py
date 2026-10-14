"""Win32 backend. Imported only on Windows by the MCP server.

NO shell invocation, networking, binary patching, or background input hacks.
PrintWindow is best-effort; GPU/accelerated windows may not render correctly.
"""
from __future__ import annotations

import ctypes as ct
from ctypes import wintypes as wt
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path
import sys
import time

from PIL import Image as PILImage
from .policy import Rect, drag_points, MAX_CAPTURE_PIXELS

if sys.platform != "win32":
    raise RuntimeError("Windows 10/11 interactive desktop required")

U = ct.WinDLL("user32", use_last_error=True)
K = ct.WinDLL("kernel32", use_last_error=True)
G = ct.WinDLL("gdi32", use_last_error=True)

# Set process-wide DPI awareness before accessing any GUI coordinates.
try:
    U.SetProcessDpiAwarenessContext.argtypes = (ct.c_void_p,)
    U.SetProcessDpiAwarenessContext.restype = wt.BOOL
    if not U.SetProcessDpiAwarenessContext(ct.c_void_p(-4)):
        ct.windll.shcore.SetProcessDpiAwareness(2)
except (AttributeError, OSError):
    try: U.SetProcessDPIAware()
    except Exception: pass

class RECT(ct.Structure):
    _fields_ = [("left", wt.LONG), ("top", wt.LONG), ("right", wt.LONG), ("bottom", wt.LONG)]

class POINT(ct.Structure):
    _fields_ = [("x", wt.LONG), ("y", wt.LONG)]

class BITMAPINFOHEADER(ct.Structure):
    _fields_ = [("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
                ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
                ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
                ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD)]

class BITMAPINFO(ct.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wt.DWORD * 3)]

class MOUSEINPUT(ct.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
                ("dwFlags", wt.DWORD), ("time", wt.DWORD), ("dwExtraInfo", ct.c_void_p)]

class KEYBDINPUT(ct.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD),
                ("time", wt.DWORD), ("dwExtraInfo", ct.c_void_p)]

class INPUTUNION(ct.Union):
    _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT)]

class INPUT(ct.Structure):
    _fields_ = [("type", wt.DWORD), ("payload", INPUTUNION)]

WINFUNCTYPE = ct.WINFUNCTYPE
WNDENUMPROC = WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

U.EnumWindows.argtypes = (WNDENUMPROC, wt.LPARAM)
U.EnumWindows.restype = wt.BOOL
U.IsWindowVisible.argtypes = (wt.HWND,)
U.IsWindowVisible.restype = wt.BOOL
U.IsIconic.argtypes = (wt.HWND,)
U.IsIconic.restype = wt.BOOL
U.GetWindowRect.argtypes = (wt.HWND, ct.POINTER(RECT))
U.GetWindowRect.restype = wt.BOOL
U.GetWindowTextLengthW.argtypes = (wt.HWND,)
U.GetWindowTextLengthW.restype = ct.c_int
U.GetWindowTextW.argtypes = (wt.HWND, wt.LPWSTR, ct.c_int)
U.GetWindowTextW.restype = ct.c_int
U.GetWindowThreadProcessId.argtypes = (wt.HWND, ct.POINTER(wt.DWORD))
U.GetWindowThreadProcessId.restype = wt.DWORD
U.PrintWindow.argtypes = (wt.HWND, wt.HDC, wt.UINT)
U.PrintWindow.restype = wt.BOOL
U.GetForegroundWindow.argtypes = ()
U.GetForegroundWindow.restype = wt.HWND
U.SetForegroundWindow.argtypes = (wt.HWND,)
U.SetForegroundWindow.restype = wt.BOOL
U.SetCursorPos.argtypes = (ct.c_int, ct.c_int)
U.SetCursorPos.restype = wt.BOOL
U.GetCursorPos.argtypes = (ct.POINTER(POINT),)
U.GetCursorPos.restype = wt.BOOL
U.SendInput.argtypes = (wt.UINT, ct.POINTER(INPUT), ct.c_int)
U.SendInput.restype = wt.UINT
U.GetAsyncKeyState.argtypes = (ct.c_int,)
U.GetAsyncKeyState.restype = wt.SHORT

K.OpenProcess.argtypes = (wt.DWORD, wt.BOOL, wt.DWORD)
K.OpenProcess.restype = wt.HANDLE
K.CloseHandle.argtypes = (wt.HANDLE,)
K.CloseHandle.restype = wt.BOOL
K.QueryFullProcessImageNameW.argtypes = (wt.HANDLE, wt.DWORD, wt.LPWSTR, ct.POINTER(wt.DWORD))
K.QueryFullProcessImageNameW.restype = wt.BOOL
G.CreateCompatibleDC.argtypes = (wt.HDC,)
G.CreateCompatibleDC.restype = wt.HDC
G.DeleteDC.argtypes = (wt.HDC,)
G.DeleteDC.restype = wt.BOOL
G.CreateDIBSection.argtypes = (wt.HDC, ct.POINTER(BITMAPINFO), wt.UINT, ct.POINTER(ct.c_void_p), wt.HANDLE, wt.DWORD)
G.CreateDIBSection.restype = wt.HBITMAP
G.DeleteObject.argtypes = (wt.HGDIOBJ,)
G.DeleteObject.restype = wt.BOOL
G.SelectObject.argtypes = (wt.HDC, wt.HGDIOBJ)
G.SelectObject.restype = wt.HGDIOBJ

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
INPUT_MOUSE = 0
INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x0002
MOUSE_LEFT_DOWN, MOUSE_LEFT_UP = 0x0002, 0x0004
MOUSE_RIGHT_DOWN, MOUSE_RIGHT_UP = 0x0008, 0x0010
MOUSE_MIDDLE_DOWN, MOUSE_MIDDLE_UP = 0x0020, 0x0040
MOUSE_X_DOWN, MOUSE_X_UP = 0x0080, 0x0100
MOUSE_WHEEL, MOUSE_HWHEEL = 0x0800, 0x1000
BUTTON_FLAGS = {
    "left": (MOUSE_LEFT_DOWN, MOUSE_LEFT_UP, 0),
    "right": (MOUSE_RIGHT_DOWN, MOUSE_RIGHT_UP, 0),
    "middle": (MOUSE_MIDDLE_DOWN, MOUSE_MIDDLE_UP, 0),
    "x1": (MOUSE_X_DOWN, MOUSE_X_UP, 1),
    "x2": (MOUSE_X_DOWN, MOUSE_X_UP, 2),
}
KEYS = {"ctrl": 0x11, "shift": 0x10, "alt": 0x12, "space": 0x20}


@dataclass(frozen=True)
class Window:
    hwnd: int
    pid: int
    exe: str
    title: str
    rect: Rect

    @property
    def id(self) -> str:
        return f"{self.pid}:{self.hwnd:x}"


def executable(pid: int) -> str | None:
    handle = K.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return None
    try:
        buf = ct.create_unicode_buffer(32768)
        n = wt.DWORD(len(buf))
        if K.QueryFullProcessImageNameW(handle, 0, buf, ct.byref(n)):
            return Path(buf.value).name.lower()
    finally:
        K.CloseHandle(handle)
    return None


def enumerate_windows(allowed: frozenset[str]) -> list[Window]:
    found: list[Window] = []

    def check(hwnd: int, _) -> bool:
        if not U.IsWindowVisible(hwnd) or U.IsIconic(hwnd):
            return True
        pid = wt.DWORD()
        U.GetWindowThreadProcessId(hwnd, ct.byref(pid))
        exe = executable(pid.value)
        if exe not in allowed:
            return True
        rect = RECT()
        if not U.GetWindowRect(hwnd, ct.byref(rect)):
            return True
        r = Rect(rect.left, rect.top, rect.right, rect.bottom)
        if r.width < 100 or r.height < 80 or r.width * r.height > MAX_CAPTURE_PIXELS:
            return True
        n = min(U.GetWindowTextLengthW(hwnd), 1000)
        buf = ct.create_unicode_buffer(n + 2)
        U.GetWindowTextW(hwnd, buf, len(buf))
        if not buf.value:
            return True
        found.append(Window(hwnd, pid.value, exe, buf.value, r))
        return True

    cb = WNDENUMPROC(check)
    if not U.EnumWindows(cb, 0):
        raise OSError(f"EnumWindows failed: {ct.get_last_error()}")
    return found


def get_window(window_id: str, allowed: frozenset[str]) -> Window:
    matches = [w for w in enumerate_windows(allowed) if w.id == window_id]
    if len(matches) != 1:
        raise ValueError("Window missing, minimized, or not allowed. Inspect windows again.")
    return matches[0]


def capture_window(win: Window) -> tuple[bytes, str, bool]:
    """Capture via PrintWindow; never fallback to occlusion-prone desktop pixels.

    Returns PNG bytes, capture method, suspicious/mostly uniform flag.
    """
    if win.rect.width * win.rect.height > MAX_CAPTURE_PIXELS:
        raise ValueError("Refusing oversized capture")
    info = BITMAPINFO()
    info.bmiHeader.biSize = ct.sizeof(BITMAPINFOHEADER)
    info.bmiHeader.biWidth = win.rect.width
    info.bmiHeader.biHeight = -win.rect.height  # top-down BGRA
    info.bmiHeader.biPlanes = 1
    info.bmiHeader.biBitCount = 32
    info.bmiHeader.biCompression = 0  # BI_RGB
    memdc = G.CreateCompatibleDC(None)
    if not memdc:
        raise OSError("CreateCompatibleDC failed")
    bitmap = None
    prev = None
    try:
        pixelptr = ct.c_void_p()
        bitmap = G.CreateDIBSection(memdc, ct.byref(info), 0, ct.byref(pixelptr), None, 0)
        if not bitmap or not pixelptr.value:
            raise OSError("CreateDIBSection failed")
        prev = G.SelectObject(memdc, bitmap)
        if not prev:
            raise OSError("SelectObject failed")
        render_method = ""
        for flag, label in [(2, "PrintWindow full"), (0, "PrintWindow basic")]:
            ct.memset(pixelptr, 0, win.rect.width * win.rect.height * 4)
            if U.PrintWindow(win.hwnd, memdc, flag):
                render_method = label
                break
        if not render_method:
            raise RuntimeError("PrintWindow failed for this app. GPU viewport may be unsupported.")
        pixels = ct.string_at(pixelptr, win.rect.width * win.rect.height * 4)
        im = PILImage.frombytes("RGBA", (win.rect.width, win.rect.height), pixels, "raw", "BGRA")
        rgb = im.convert("RGB")
        # A basic blank-frame guard, not visual correctness certification.
        tiny = rgb.resize((64, 64))
        extrema = tiny.getextrema()
        possibly_blank = all(hi - lo < 8 for lo, hi in extrema)
        out = BytesIO()
        rgb.save(out, format="PNG", optimize=True)
        return out.getvalue(), render_method, possibly_blank
    finally:
        if prev:
            G.SelectObject(memdc, prev)
        if bitmap:
            G.DeleteObject(bitmap)
        G.DeleteDC(memdc)


def _send(inp: INPUT) -> None:
    result = U.SendInput(1, ct.byref(inp), ct.sizeof(INPUT))
    if result != 1:
        raise OSError(f"SendInput failed. Error={ct.get_last_error()}; UIPI may block elevated target")


def _mouse(flags: int, data: int = 0) -> None:
    inp = INPUT(type=INPUT_MOUSE)
    inp.payload.mi.dwFlags = flags
    inp.payload.mi.mouseData = data & 0xffffffff
    _send(inp)


def _key(name: str, is_down: bool) -> None:
    inp = INPUT(type=INPUT_KEYBOARD)
    inp.payload.ki.wVk = KEYS[name]
    inp.payload.ki.dwFlags = 0 if is_down else KEYEVENTF_KEYUP
    _send(inp)


def _position(x: int, y: int) -> None:
    if not U.SetCursorPos(int(x), int(y)):
        raise OSError(f"SetCursorPos failed: {ct.get_last_error()}")


def _stop_if_needed() -> None:
    if U.GetAsyncKeyState(0x7B) & 0x8000:  # F12 abort
        raise InterruptedError("F12 emergency abort")


def _release_all() -> None:
    """Best effort to avoid stuck drag and modifiers, even after exceptions."""
    for b in ("left", "right", "middle", "x1", "x2"):
        try: _mouse(BUTTON_FLAGS[b][1], BUTTON_FLAGS[b][2])
        except Exception: pass
    for mod in KEYS:
        try: _key(mod, False)
        except Exception: pass


def release_all() -> None:
    _release_all()


def _activate(win: Window) -> None:
    if not U.SetForegroundWindow(win.hwnd):
        # OS may reject focus stealing. Stop rather than click the wrong app.
        raise RuntimeError("Cannot activate target; bring it to foreground manually")
    time.sleep(0.10)
    if U.GetForegroundWindow() != win.hwnd:
        raise RuntimeError("Foreground changed: refuse mouse action")


def apply_mouse(win: Window, action: str, button: str, x: int, y: int,
                end_x: int, end_y: int, duration_ms: int, steps: int,
                wheel_ticks: int, wheel_axis: str, modifiers: tuple[str, ...]) -> None:
    """One finite operation; modifier keys and all buttons are released in finally.

    Does NOT run in the background on same interactive desktop. Win32 global input.
    """
    sx, sy = win.rect.absolute(x, y)
    ex, ey = win.rect.absolute(end_x, end_y)
    down, up, mouse_data = BUTTON_FLAGS[button]
    _activate(win)
    try:
        _stop_if_needed()
        for m in modifiers:
            _key(m, True)
        _position(sx, sy)
        if action == "move":
            return
        if action in ("click", "double_click"):
            count = 2 if action == "double_click" else 1
            for _ in range(count):
                _stop_if_needed()
                _mouse(down, mouse_data)
                time.sleep(0.04)
                _mouse(up, mouse_data)
                if count == 2:
                    time.sleep(0.06)
        elif action in ("drag", "hold"):
            _mouse(down, mouse_data)
            if action == "drag":
                points = drag_points((sx, sy), (ex, ey), steps)
                sleep_s = duration_ms / 1000 / max(1, len(points) - 1)
                for px, py in points[1:]:
                    _stop_if_needed()
                    # Check focus for longer CAD drags, do not drag other windows.
                    if U.GetForegroundWindow() != win.hwnd:
                        raise RuntimeError("Target lost focus during drag")
                    _position(px, py)
                    time.sleep(sleep_s)
            else:
                endtime = time.monotonic() + duration_ms / 1000
                while time.monotonic() < endtime:
                    _stop_if_needed()
                    time.sleep(0.04)
            _mouse(up, mouse_data)
        elif action == "scroll":
            flag = MOUSE_WHEEL if wheel_axis == "vertical" else MOUSE_HWHEEL
            _mouse(flag, wheel_ticks * 120)
        else:
            raise ValueError("Unexpected action")
    finally:
        _release_all()
