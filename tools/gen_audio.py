#!/usr/bin/env python3
"""Generator dzwiekow i muzyki do Otchlani (port z legacy audio.py; numpy + wave).

Uzycie: python3 tools/gen_audio.py   -> audio/sfx/*.wav, audio/music/*.wav
"""
import os
import wave
import numpy as np

SR = 44100
LEGACY_SR = 22050          # filtr dolnoprzepustowy byl strojony dla 22050 Hz
SFX_PEAK = 10 ** (-3 / 20)   # -3 dBFS
MUSIC_PEAK = 10 ** (-6 / 20)  # -6 dBFS
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
RNG = np.random.default_rng(1234)


def tone(dur, f0, f1=None, wave="sq", vol=0.5, noise=0.0, attack=0.004, curve=2.0,
         vibrato=0.0, lowpass=0.0):
    n = max(1, int(SR * dur))
    f1 = f0 if f1 is None else f1
    i = np.arange(n)
    t = i / n
    f = f0 + (f1 - f0) * t
    if vibrato:
        f = f * (1 + 0.03 * np.sin(i / SR * vibrato * 2 * np.pi))
    phase = np.cumsum(f / SR)
    p = phase - np.floor(phase)
    if wave == "sq":
        s = np.where(p < 0.5, 1.0, -1.0)
    elif wave == "sine":
        s = np.sin(p * 2 * np.pi)
    elif wave == "saw":
        s = 2 * p - 1
    elif wave == "tri":
        s = 4 * np.abs(p - 0.5) - 1
    else:
        s = np.zeros(n)
    if noise:
        s = s * (1 - noise) + (RNG.random(n) * 2 - 1) * noise
    if lowpass:
        a = 1 - (1 - lowpass) ** (LEGACY_SR / SR)   # ta sama czestotliwosc graniczna co w 22.05 kHz
        out = np.empty(n)
        y = 0.0
        sl = s.tolist()
        for k in range(n):
            y += (sl[k] - y) * a
            out[k] = y
        s = out
    att = max(1, int(attack * SR))
    env = np.ones(n)
    env[:att] = np.arange(att) / att
    env *= (1 - t) ** curve
    return s * env * vol


def mix(*parts, offsets=None):
    offsets = offsets or [0] * len(parts)
    n = max(len(p) + o for p, o in zip(parts, offsets))
    out = np.zeros(n)
    for p, o in zip(parts, offsets):
        out[o:o + len(p)] += p
    return out


def seq(notes, dur, wave="sq", vol=0.3, curve=1.5):
    return np.concatenate([tone(dur, f, wave=wave, vol=vol, curve=curve) for f in notes])


def cat(*parts):
    return np.concatenate(parts)


def build_sfx():
    T = tone
    S = {}
    S["shoot"] = T(0.07, 900, 380, "sq", 0.16, curve=2.5)
    S["shoot_heavy"] = T(0.12, 260, 90, "saw", 0.28, noise=0.2, curve=2)
    S["shoot_magic"] = T(0.12, 1300, 500, "sine", 0.25, curve=1.5)
    S["shoot_arrow"] = T(0.08, 1500, 700, "tri", 0.22, noise=0.25, curve=2)
    S["shoot_dark"] = T(0.12, 500, 150, "sq", 0.18, noise=0.15, curve=2, lowpass=0.4)
    S["shoot_flask"] = T(0.1, 600, 900, "sine", 0.25, curve=2)
    S["enemy_shoot"] = T(0.07, 640, 300, "sq", 0.09, curve=2, lowpass=0.5)
    S["hit"] = T(0.06, 300, 120, "sq", 0.18, noise=0.6, curve=2, lowpass=0.5)
    S["crit"] = mix(T(0.08, 1200, 600, "sq", 0.15, curve=2), T(0.08, 300, 100, "saw", 0.2, noise=0.5))
    S["enemy_die"] = mix(T(0.18, 420, 70, "sq", 0.2, curve=1.5, lowpass=0.35),
                         T(0.12, 200, 100, "sine", noise=1.0, vol=0.15, lowpass=0.3))
    S["player_hurt"] = T(0.3, 220, 60, "saw", 0.4, noise=0.35, curve=1.5, lowpass=0.4)
    S["coin"] = mix(T(0.06, 1318, wave="sine", vol=0.25, curve=1),
                    T(0.14, 1760, wave="sine", vol=0.25, curve=1.5), offsets=[0, int(0.05 * SR)])
    S["xp"] = T(0.05, 1500, 2100, "sine", 0.1, curve=1.5)
    S["levelup"] = cat(seq([523, 659, 784, 1046, 1318], 0.07, "sq", 0.16, 1.2),
                       T(0.25, 1568, wave="sine", vol=0.25, curve=1.5))
    S["dash"] = T(0.16, 600, 200, "sine", noise=1.0, vol=0.35, curve=1.2, lowpass=0.25)
    S["door"] = mix(T(0.25, 110, 70, "sq", 0.2, curve=1.2, lowpass=0.3),
                    T(0.2, 300, 200, "sine", noise=1.0, vol=0.12, curve=2.0, lowpass=0.15))
    S["door_open"] = seq([392, 523, 659], 0.06, "tri", 0.25, 1.2)
    S["explosion"] = mix(T(0.55, 120, 30, "sine", noise=1.0, vol=0.6, curve=1.6, lowpass=0.18),
                         T(0.35, 90, 40, "sine", 0.5, curve=2))
    S["item"] = cat(seq([392, 494, 587, 784, 988], 0.08, "tri", 0.3, 1.0),
                    T(0.4, 1175, wave="sine", vol=0.3, curve=1.5))
    S["boss_roar"] = T(1.1, 95, 45, "saw", 0.45, noise=0.25, vibrato=7, curve=1.0, lowpass=0.25)
    S["lightning"] = T(0.18, 2000, 400, "sq", noise=1.0, vol=0.3, curve=1.5, lowpass=0.7)
    S["shield"] = T(0.25, 500, 1100, "sine", 0.3, curve=1.2)
    S["block"] = mix(T(0.12, 1500, 900, "tri", 0.25, curve=2), T(0.1, 400, 300, "sq", 0.1, curve=2))
    S["select"] = T(0.04, 700, wave="tri", vol=0.18, curve=1.5)
    S["confirm"] = seq([660, 990], 0.06, "tri", 0.25, 1.3)
    S["deny"] = T(0.15, 180, 140, "sq", 0.18, curve=1.0, lowpass=0.4)
    S["heal"] = T(0.3, 500, 1000, "sine", 0.3, curve=1.2, vibrato=8)
    S["spawn"] = T(0.2, 220, 520, "sine", 0.12, curve=1.0)
    S["ability"] = mix(T(0.35, 180, 900, "saw", 0.2, curve=1.2, lowpass=0.5),
                       T(0.35, 90, 450, "sine", 0.25, curve=1.2))
    S["buy"] = seq([880, 1175, 1568], 0.06, "sine", 0.3, 1.2)
    S["dodge"] = T(0.1, 1200, 1800, "sine", 0.15, curve=1.5)
    S["thunk"] = T(0.08, 160, 80, "sq", 0.15, noise=0.3, curve=2, lowpass=0.4)
    S["break"] = T(0.15, 300, 80, "sine", noise=1.0, vol=0.3, curve=1.5, lowpass=0.35)
    S["charge"] = T(0.45, 200, 600, "saw", 0.12, curve=0.6, lowpass=0.4)
    S["slam"] = mix(T(0.4, 80, 30, "sine", 0.6, curve=1.5),
                    T(0.3, 200, 50, "sine", noise=1.0, vol=0.35, curve=2.0, lowpass=0.2))
    S["laser"] = T(0.3, 900, 880, "sq", 0.1, curve=0.8, vibrato=20, lowpass=0.6)
    S["victory"] = cat(seq([523, 659, 784, 1046, 784, 1046, 1318], 0.12, "tri", 0.3, 0.8),
                       T(0.8, 1568, wave="sine", vol=0.3, curve=1.2))
    S["gameover"] = cat(seq([392, 370, 349, 330], 0.2, "tri", 0.3, 0.5),
                        T(0.9, 262, 200, "tri", 0.3, curve=1.0))
    S["trapdoor"] = T(0.6, 400, 80, "sine", 0.3, curve=0.8, vibrato=5)
    S["poison"] = T(0.2, 300, 200, "sine", noise=1.0, vol=0.15, curve=1.0, lowpass=0.1)
    return S


# ---------------------------------------------------------------- muzyka
def note_freq(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def render_track(bpm, chords, bass_pattern, arp_pattern, drums=True, intensity=1.0):
    """Jedna petla; ogony nut przekraczajace koniec sa zawijane na poczatek (bezszwowo)."""
    beat = 60.0 / bpm
    eighth = beat / 2
    total = int(SR * beat * 4 * len(chords))
    tail = int(SR * 1.5)
    out = np.zeros(total + tail)
    cache = {}

    def note(n, dur, wave, vol, curve):
        key = (n, round(dur, 3), wave, vol, curve)
        if key not in cache:
            cache[key] = tone(dur, note_freq(n), wave=wave, vol=vol, curve=curve,
                              lowpass=0.35 if wave in ("saw", "sq") else 0.0)
        return cache[key]

    def add(samples, start):
        out[start:start + len(samples)] += samples

    hat = tone(0.04, 100, wave="sine", vol=1.0, noise=1.0, curve=3) * 0.06
    kick = tone(0.18, 120, 40, "sine", 0.5, curve=2.0)
    for b, chord in enumerate(chords):
        root = chord[0]
        bar_start = int(b * beat * 4 * SR)
        for i, step in enumerate(bass_pattern):
            if step is None:
                continue
            add(note(root - 24 + step, eighth * 1.6, "tri", 0.32 * intensity, 1.2), bar_start + int(i * eighth * SR))
        for i, idx in enumerate(arp_pattern):
            if idx is None:
                continue
            n = chord[idx % len(chord)] + 12 * (idx // len(chord))
            add(note(n, eighth * 0.9, "sq", 0.07 * intensity, 2.0), bar_start + int(i * eighth * SR))
        if drums:
            for i in range(8):
                st = bar_start + int(i * eighth * SR)
                if i % 2 == 1:
                    add(hat, st)
                if i in (0, 4) or (intensity > 1.0 and i in (3, 6)):
                    add(kick, st)
    loop = out[:total].copy()
    loop[:tail] += out[total:]   # zawiniecie ogona
    return loop


def build_music():
    M = {}
    # a-moll: Am F C G ...
    M["explore"] = (render_track(
        112,
        [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62],
         [57, 60, 64], [53, 57, 60], [52, 55, 59], [52, 56, 59]],
        [0, None, 0, None, 7, None, 0, 12],
        [0, 1, 2, 3, 2, 1, 4, 2]), 2)      # 2 powtorzenia ~34 s
    M["boss"] = (render_track(
        148,
        [[50, 53, 57], [46, 50, 53], [48, 52, 55], [45, 49, 52],
         [50, 53, 57], [46, 50, 53], [43, 46, 50], [45, 49, 52]],
        [0, 0, 12, 0, 0, 12, 0, 7],
        [0, 2, 1, 3, 0, 2, 4, 3],
        intensity=1.25), 4)                 # ~52 s
    M["menu"] = (render_track(
        84,
        [[57, 60, 64], [55, 59, 62], [53, 57, 60], [52, 56, 59]],
        [0, None, None, None, 7, None, None, None],
        [0, None, 2, None, 1, None, 3, None],
        drums=False), 4)                    # ~46 s
    return M


# ---------------------------------------------------------------- zapis
def write_wav(path, x, peak, fade_ms=None):
    x = np.nan_to_num(np.asarray(x, dtype=np.float64))
    if fade_ms:
        nf = min(len(x) // 2, int(SR * fade_ms / 1000))
        ramp = np.linspace(0, 1, nf)
        x[:nf] *= ramp
        x[-nf:] *= ramp[::-1]
    m = np.max(np.abs(x))
    if m > 0:
        x = x / m * peak
    pcm = np.round(x * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def main():
    sfx_dir = os.path.join(ROOT, "audio", "sfx")
    mus_dir = os.path.join(ROOT, "audio", "music")
    os.makedirs(sfx_dir, exist_ok=True)
    os.makedirs(mus_dir, exist_ok=True)
    for name, x in build_sfx().items():
        write_wav(os.path.join(sfx_dir, name + ".wav"), x, SFX_PEAK, fade_ms=3)
        print("sfx", name)
    for name, (loop, reps) in build_music().items():
        write_wav(os.path.join(mus_dir, name + ".wav"), np.tile(loop, reps), MUSIC_PEAK)  # bez fade: petla
        print("music", name)


if __name__ == "__main__":
    main()
