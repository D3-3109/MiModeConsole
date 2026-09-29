import ctypes
import ctypes.wintypes as wt
import time

from PIL import ImageGrab

user32 = ctypes.windll.user32
try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    user32.SetProcessDPIAware()

TITLE = "Redmi Book 模式控制台"
hwnd = user32.FindWindowW(None, TITLE)
if not hwnd:
    print("ERROR: window not found")
    raise SystemExit(1)

if user32.IsIconic(hwnd):
    user32.ShowWindow(hwnd, 9)

# force to foreground: SwitchToThisWindow, then alt-key trick as fallback
user32.SwitchToThisWindow(hwnd, True)
time.sleep(0.3)
if user32.GetForegroundWindow() != hwnd:
    user32.keybd_event(0x12, 0, 0, 0)  # Alt down
    user32.SetForegroundWindow(hwnd)
    user32.keybd_event(0x12, 0, 2, 0)  # Alt up
time.sleep(0.5)

fg = user32.GetForegroundWindow()
print("foreground is target:", fg == hwnd)

rect = wt.RECT()
user32.GetWindowRect(hwnd, ctypes.byref(rect))
img = ImageGrab.grab(bbox=(rect.left, rect.top, rect.right, rect.bottom))
out = r"C:\Users\Jason\NBFC\window_shot.png"
img.save(out)
print("saved", out, "size", img.size)
