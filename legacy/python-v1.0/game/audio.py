"""Proceduralnie generowane efekty dźwiękowe i muzyka (bez plików audio)."""
import math
import random
import threading
from array import array

import pygame


class Audio:
    def __init__(self):
        self.ok = False
        self.sounds = {}
        self.music = {}
        self.sfx_vol = 0.7
        self.music_vol = 0.45
        self.sr = 22050
        self.channels = 1
        self.music_channel = None
        self.current_music = None
        self.wanted_music = None
        self._last_play = {}
        self._lock = threading.Lock()

    # ------------------------------------------------------------------ init
    def init(self, sfx_vol=0.7, music_vol=0.45):
        self.sfx_vol = sfx_vol
        self.music_vol = music_vol
        try:
            if not pygame.mixer.get_init():
                pygame.mixer.init(22050, -16, 1, 512)
            info = pygame.mixer.get_init()
            if not info:
                return
            self.sr, size, self.channels = info
            if size not in (-16, 16):
                return
            pygame.mixer.set_num_channels(32)
            self.music_channel = pygame.mixer.Channel(31)
            pygame.mixer.set_reserved(1)
            self.ok = True
        except Exception:
            self.ok = False
            return
        self._build_sfx()
        t = threading.Thread(target=self._build_music, daemon=True)
        t.start()

    # ------------------------------------------------------------------ synteza
    def _to_sound(self, samples):
        """samples: lista floatów -1..1"""
        buf = array("h")
        if self.channels == 2:
            for s in samples:
                v = int(max(-1.0, min(1.0, s)) * 32000)
                buf.append(v)
                buf.append(v)
        else:
            buf.extend(int(max(-1.0, min(1.0, s)) * 32000) for s in samples)
        return pygame.mixer.Sound(buffer=buf.tobytes())

    def _tone(self, dur, f0, f1=None, wave="sq", vol=0.5, noise=0.0, attack=0.004, curve=2.0,
              vibrato=0.0, lowpass=0.0):
        sr = self.sr
        n = max(1, int(sr * dur))
        out = [0.0] * n
        f1 = f0 if f1 is None else f1
        phase = 0.0
        lp = 0.0
        att = max(1, int(attack * sr))
        rnd = random.random
        for i in range(n):
            t = i / n
            f = f0 + (f1 - f0) * t
            if vibrato:
                f *= 1 + 0.03 * math.sin(i / sr * vibrato * math.tau)
            phase += f / sr
            p = phase - int(phase)
            if wave == "sq":
                s = 1.0 if p < 0.5 else -1.0
            elif wave == "sine":
                s = math.sin(p * math.tau)
            elif wave == "saw":
                s = 2 * p - 1
            elif wave == "tri":
                s = 4 * abs(p - 0.5) - 1
            else:
                s = 0.0
            if noise:
                s = s * (1 - noise) + (rnd() * 2 - 1) * noise
            if lowpass:
                lp += (s - lp) * lowpass
                s = lp
            env = (i / att if i < att else 1.0) * (1 - t) ** curve
            out[i] = s * env * vol
        return out

    @staticmethod
    def _mix(*parts, offsets=None):
        offsets = offsets or [0] * len(parts)
        n = max(len(p) + o for p, o in zip(parts, offsets))
        out = [0.0] * n
        for p, o in zip(parts, offsets):
            for i, v in enumerate(p):
                out[i + o] += v
        return out

    def _seq(self, notes, dur, wave="sq", vol=0.3, curve=1.5):
        out = []
        for f in notes:
            out.extend(self._tone(dur, f, wave=wave, vol=vol, curve=curve))
        return out

    def _build_sfx(self):
        T = self._tone
        sr = self.sr
        S = {}
        S["shoot"] = T(0.07, 900, 380, "sq", 0.16, curve=2.5)
        S["shoot_heavy"] = T(0.12, 260, 90, "saw", 0.28, noise=0.2, curve=2)
        S["shoot_magic"] = T(0.12, 1300, 500, "sine", 0.25, curve=1.5)
        S["shoot_arrow"] = T(0.08, 1500, 700, "tri", 0.22, noise=0.25, curve=2)
        S["shoot_dark"] = T(0.12, 500, 150, "sq", 0.18, noise=0.15, curve=2, lowpass=0.4)
        S["shoot_flask"] = T(0.1, 600, 900, "sine", 0.25, curve=2)
        S["enemy_shoot"] = T(0.07, 640, 300, "sq", 0.09, curve=2, lowpass=0.5)
        S["hit"] = T(0.06, 300, 120, "sq", 0.18, noise=0.6, curve=2, lowpass=0.5)
        S["crit"] = self._mix(T(0.08, 1200, 600, "sq", 0.15, curve=2), T(0.08, 300, 100, "saw", 0.2, noise=0.5))
        S["enemy_die"] = self._mix(T(0.18, 420, 70, "sq", 0.2, curve=1.5, lowpass=0.35), T(0.12, 200, 100, "sine", noise=1.0, vol=0.15, lowpass=0.3))
        S["player_hurt"] = T(0.3, 220, 60, "saw", 0.4, noise=0.35, curve=1.5, lowpass=0.4)
        S["coin"] = self._mix(T(0.06, 1318, wave="sine", vol=0.25, curve=1), T(0.14, 1760, wave="sine", vol=0.25, curve=1.5), offsets=[0, int(0.05 * sr)])
        S["xp"] = T(0.05, 1500, 2100, "sine", 0.1, curve=1.5)
        S["levelup"] = self._seq([523, 659, 784, 1046, 1318], 0.07, "sq", 0.16, 1.2) + T(0.25, 1568, wave="sine", vol=0.25, curve=1.5)
        S["dash"] = T(0.16, 600, 200, "sine", noise=1.0, vol=0.35, curve=1.2, lowpass=0.25)
        S["door"] = self._mix(T(0.25, 110, 70, "sq", 0.2, curve=1.2, lowpass=0.3), T(0.2, 300, 200, "sine", noise=1.0, vol=0.12, lowpass=0.15))
        S["door_open"] = self._seq([392, 523, 659], 0.06, "tri", 0.25, 1.2)
        S["explosion"] = self._mix(T(0.55, 120, 30, "sine", noise=1.0, vol=0.6, curve=1.6, lowpass=0.18), T(0.35, 90, 40, "sine", 0.5, curve=2))
        S["item"] = self._seq([392, 494, 587, 784, 988], 0.08, "tri", 0.3, 1.0) + T(0.4, 1175, wave="sine", vol=0.3, curve=1.5)
        S["boss_roar"] = T(1.1, 95, 45, "saw", 0.45, noise=0.25, vibrato=7, curve=1.0, lowpass=0.25)
        S["lightning"] = T(0.18, 2000, 400, "sq", noise=1.0, vol=0.3, curve=1.5, lowpass=0.7)
        S["shield"] = T(0.25, 500, 1100, "sine", 0.3, curve=1.2)
        S["block"] = self._mix(T(0.12, 1500, 900, "tri", 0.25, curve=2), T(0.1, 400, 300, "sq", 0.1, curve=2))
        S["select"] = T(0.04, 700, wave="tri", vol=0.18, curve=1.5)
        S["confirm"] = self._seq([660, 990], 0.06, "tri", 0.25, 1.3)
        S["deny"] = T(0.15, 180, 140, "sq", 0.18, curve=1.0, lowpass=0.4)
        S["heal"] = T(0.3, 500, 1000, "sine", 0.3, curve=1.2, vibrato=8)
        S["spawn"] = T(0.2, 220, 520, "sine", 0.12, curve=1.0)
        S["ability"] = self._mix(T(0.35, 180, 900, "saw", 0.2, curve=1.2, lowpass=0.5), T(0.35, 90, 450, "sine", 0.25, curve=1.2))
        S["buy"] = self._seq([880, 1175, 1568], 0.06, "sine", 0.3, 1.2)
        S["dodge"] = T(0.1, 1200, 1800, "sine", 0.15, curve=1.5)
        S["thunk"] = T(0.08, 160, 80, "sq", 0.15, noise=0.3, curve=2, lowpass=0.4)
        S["break"] = T(0.15, 300, 80, "sine", noise=1.0, vol=0.3, curve=1.5, lowpass=0.35)
        S["charge"] = T(0.45, 200, 600, "saw", 0.12, curve=0.6, lowpass=0.4)
        S["slam"] = self._mix(T(0.4, 80, 30, "sine", 0.6, curve=1.5), T(0.3, 200, 50, "sine", noise=1.0, vol=0.35, lowpass=0.2))
        S["laser"] = T(0.3, 900, 880, "sq", 0.1, curve=0.8, vibrato=20, lowpass=0.6)
        S["victory"] = self._seq([523, 659, 784, 1046, 784, 1046, 1318], 0.12, "tri", 0.3, 0.8) + T(0.8, 1568, wave="sine", vol=0.3, curve=1.2)
        S["gameover"] = self._seq([392, 370, 349, 330], 0.2, "tri", 0.3, 0.5) + T(0.9, 262, 200, "tri", 0.3, curve=1.0)
        S["trapdoor"] = T(0.6, 400, 80, "sine", 0.3, curve=0.8, vibrato=5)
        S["poison"] = T(0.2, 300, 200, "sine", noise=1.0, vol=0.15, curve=1.0, lowpass=0.1)
        for k, v in S.items():
            try:
                self.sounds[k] = self._to_sound(v)
            except Exception:
                pass

    # ------------------------------------------------------------------ muzyka
    def _note_freq(self, n):
        return 440.0 * 2 ** ((n - 69) / 12)

    def _render_track(self, bpm, chords, bass_pattern, arp_pattern, drums=True, intensity=1.0):
        sr = self.sr
        beat = 60.0 / bpm
        eighth = beat / 2
        bars = len(chords)
        total = int(sr * beat * 4 * bars)
        out = [0.0] * total
        cache = {}

        def note(n, dur, wave, vol, curve):
            key = (n, round(dur, 3), wave, vol, curve)
            if key not in cache:
                cache[key] = self._tone(dur, self._note_freq(n), wave=wave, vol=vol, curve=curve,
                                        lowpass=0.35 if wave in ("saw", "sq") else 0.0)
            return cache[key]

        def add(samples, start):
            end = min(total, start + len(samples))
            for i in range(start, end):
                out[i] += samples[i - start]

        hat = [v * 0.06 for v in self._tone(0.04, 100, wave="sine", vol=1.0, noise=1.0, curve=3)]
        kick = self._tone(0.18, 120, 40, "sine", 0.5, curve=2.0)
        for b, chord in enumerate(chords):
            root = chord[0]
            bar_start = int(b * beat * 4 * sr)
            for i, step in enumerate(bass_pattern):
                if step is None:
                    continue
                st = bar_start + int(i * eighth * sr)
                add(note(root - 24 + step, eighth * 1.6, "tri", 0.32 * intensity, 1.2), st)
            for i, idx in enumerate(arp_pattern):
                if idx is None:
                    continue
                st = bar_start + int(i * eighth * sr)
                n = chord[idx % len(chord)] + 12 * (idx // len(chord))
                add(note(n, eighth * 0.9, "sq", 0.07 * intensity, 2.0), st)
            if drums:
                for i in range(8):
                    st = bar_start + int(i * eighth * sr)
                    if i % 2 == 1:
                        add(hat, st)
                    if i in (0, 4) or (intensity > 1.0 and i in (3, 6)):
                        add(kick, st)
        return out

    def _build_music(self):
        try:
            # a-moll: Am F C G
            explore = self._render_track(
                112,
                [[57, 60, 64], [53, 57, 60], [48, 52, 55], [55, 59, 62],
                 [57, 60, 64], [53, 57, 60], [52, 55, 59], [52, 56, 59]],
                [0, None, 0, None, 7, None, 0, 12],
                [0, 1, 2, 3, 2, 1, 4, 2],
            )
            snd = self._to_sound(explore)
            with self._lock:
                self.music["explore"] = snd
            boss = self._render_track(
                148,
                [[50, 53, 57], [46, 50, 53], [48, 52, 55], [45, 49, 52],
                 [50, 53, 57], [46, 50, 53], [43, 46, 50], [45, 49, 52]],
                [0, 0, 12, 0, 0, 12, 0, 7],
                [0, 2, 1, 3, 0, 2, 4, 3],
                intensity=1.25,
            )
            snd = self._to_sound(boss)
            with self._lock:
                self.music["boss"] = snd
            menu = self._render_track(
                84,
                [[57, 60, 64], [55, 59, 62], [53, 57, 60], [52, 56, 59]],
                [0, None, None, None, 7, None, None, None],
                [0, None, 2, None, 1, None, 3, None],
                drums=False, intensity=1.0,
            )
            snd = self._to_sound(menu)
            with self._lock:
                self.music["menu"] = snd
        except Exception:
            pass

    # ------------------------------------------------------------------ odtwarzanie
    def play(self, name, vol=1.0, min_gap=0.03):
        if not self.ok or self.sfx_vol <= 0:
            return
        s = self.sounds.get(name)
        if s is None:
            return
        now = pygame.time.get_ticks() / 1000.0
        if now - self._last_play.get(name, -1) < min_gap:
            return
        self._last_play[name] = now
        ch = pygame.mixer.find_channel(False)
        if ch is None:
            return
        ch.set_volume(max(0.0, min(1.0, vol * self.sfx_vol)))
        ch.play(s)

    def play_music(self, name):
        self.wanted_music = name
        self._try_music()

    def _try_music(self):
        if not self.ok or self.music_channel is None:
            return
        name = self.wanted_music
        if name == self.current_music and self.music_channel.get_busy():
            return
        with self._lock:
            snd = self.music.get(name)
        if snd is None:
            return
        self.current_music = name
        self.music_channel.set_volume(self.music_vol)
        self.music_channel.play(snd, loops=-1, fade_ms=600)

    def update(self):
        """Wywoływane co klatkę – uruchamia muzykę gdy skończy się generować."""
        if self.ok and self.wanted_music and self.wanted_music != self.current_music:
            self._try_music()

    def stop_music(self, fade=400):
        self.wanted_music = None
        self.current_music = None
        if self.ok and self.music_channel:
            self.music_channel.fadeout(fade)

    def set_volumes(self, sfx, music):
        self.sfx_vol = sfx
        self.music_vol = music
        if self.ok and self.music_channel:
            self.music_channel.set_volume(music)


audio = Audio()
