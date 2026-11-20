"""Zapis postępów (odblokowania, rekordy, ustawienia)."""
import json
import os

from .config import save_dir

DEFAULT = {
    "unlocked": ["rycerz", "lowczyni", "berserker", "mag", "zlodziej"],
    "best": {},       # postać -> trudność -> najlepsze piętro
    "wins": {},       # postać -> lista trudności z wygraną
    "stats": {"runs": 0, "kills": 0, "wins": 0, "deaths": 0, "items": 0},
    "settings": {"sfx": 0.7, "music": 0.45, "fullscreen": False, "shake": True,
                 "dmgnums": True, "autoaim": False},
    "last": {"char": "rycerz", "diff": 1},
}


def _path():
    return os.path.join(save_dir(), "save.json")


def load():
    data = json.loads(json.dumps(DEFAULT))
    try:
        with open(_path(), "r", encoding="utf-8") as f:
            loaded = json.load(f)
        for k, v in loaded.items():
            if isinstance(v, dict) and isinstance(data.get(k), dict):
                data[k].update(v)
            else:
                data[k] = v
        for c in DEFAULT["unlocked"]:
            if c not in data["unlocked"]:
                data["unlocked"].append(c)
    except Exception:
        pass
    return data


def save(data):
    try:
        tmp = _path() + ".tmp"
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(data, f, ensure_ascii=False, indent=1)
        os.replace(tmp, _path())
    except Exception:
        pass
