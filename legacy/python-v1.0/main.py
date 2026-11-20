"""Otchłań – roguelike w stylu The Binding of Isaac / Brotato.

Uruchomienie:  python main.py   (wymaga: pip install pygame-ce)
"""
import os
import sys
import traceback

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def _windows_setup():
    if os.name != "nt":
        return
    try:
        import ctypes
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except Exception:
            ctypes.windll.user32.SetProcessDPIAware()
    except Exception:
        pass


def _crash(exc_text):
    path = None
    try:
        from game.config import save_dir
        path = os.path.join(save_dir(), "crash.log")
        with open(path, "w", encoding="utf-8") as f:
            f.write(exc_text)
    except Exception:
        pass
    if sys.stdout:
        print(exc_text)
        if path:
            print("Szczegóły błędu zapisano w:", path)
    if os.name == "nt":
        try:
            import ctypes
            msg = "Gra napotkała błąd i musi zostać zamknięta.\n\n" + exc_text.strip().splitlines()[-1]
            if path:
                msg += "\n\nSzczegóły zapisano w:\n" + path
            ctypes.windll.user32.MessageBoxW(None, msg, "Otchłań – błąd", 0x10)
        except Exception:
            pass


if __name__ == "__main__":
    _windows_setup()
    try:
        from game.app import main
        main()
    except SystemExit:
        raise
    except Exception:
        _crash(traceback.format_exc())
        sys.exit(1)
