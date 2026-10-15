#!/usr/bin/env python3
"""Synthesises AVABosconian's sound effects and tunes into ../src/AVABosconian/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVABosconian", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # Guns: the ship's twin shot, and the enemy's.
    write("Fire.wav", mix(tone(0.1, 1800, 500, "square", release=1.0), tone(0.1, 1500, 420, "square", release=1.0, gain=0.6)), 0.22)
    write("EnemyFire.wav", tone(0.12, 600, 1000, "pulse", release=0.8), 0.18)
    # Bases: a pod knocked out, and the whole base going up.
    write("PodHit.wav", mix(tone(0.2, 900, 200, "square", release=0.8), tone(0.15, 3000, 800, "noise", release=0.9, gain=0.5)), 0.36)
    write("BaseBoom.wav", mix(tone(1.6, 3000, 60, "noise", release=0.5), tone(1.6, 140, 30, "saw", release=0.5, gain=0.8),
                              cat(silence(0.1), *[tone(0.12, f, f * 0.6, "square", release=0.6, gain=0.4) for f in [880, 660, 520, 400, 300]])), 0.55)
    write("EnemyHit.wav", mix(tone(0.25, 2400, 300, "noise", release=0.8), tone(0.2, 700, 120, "square", release=0.8, gain=0.6)), 0.34)
    write("Rock.wav", tone(0.2, 900, 200, "noise", release=0.9), 0.3)
    write("MineBoom.wav", mix(tone(0.6, 2000, 80, "noise", release=0.6), tone(0.5, 220, 50, "saw", release=0.7, gain=0.7)), 0.46)
    # The spy, its escape, the squadron and its leader brought down.
    write("Spy.wav", tone(0.8, 900, 1400, "tri", release=0.4, vibrato=0.08, vib_rate=14), 0.26)
    write("SpyAway.wav", cat(tone(0.2, 1400, 2200, "tri", release=0.5), tone(0.5, 2200, 4000, "tri", release=0.8)), 0.28)
    write("Formation.wav", cat(*[tone(0.12, f, kind="square", release=0.2) for f in [700, 900, 700, 900]]), 0.28)
    write("Leader.wav", cat(mix(tone(0.3, 2400, 300, "noise", release=0.8), tone(0.3, 600, 100, "square", release=0.8, gain=0.6)),
                            *[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["E6", "G6", "B6", "E7"]]), 0.42)
    # The alert: a falling two-tone siren.
    write("Alert.wav", cat(*[tone(0.22, f0, f1, "saw", release=0.2, gain=0.8) for f0, f1 in [(1200, 800), (1200, 800), (1200, 800)]]), 0.3)
    write("Death.wav", cat(mix(tone(0.9, 3000, 100, "noise", release=0.6), tone(0.9, 500, 40, "saw", release=0.6, gain=0.7)),
                           *[tone(0.14, hz(n), kind="tri", release=0.4) for n in ["D5", "Bb4", "G4", "D4"]]), 0.48)
    write("RoundStart.wav", tune(170, [("D5", 0.5), ("A5", 0.5), ("D6", 0.5), ("C6", 0.5), ("A5", 0.5), ("D6", 1.5)], [("D3", 1), ("A2", 1), ("D3", 1.5)]), 0.42)
    write("Clear.wav", tune(176, [("D6", 0.5), ("A5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5), ("E6", 0.5), ("F6", 0.5), ("A6", 2)], [("D3", 1), ("F3", 1), ("A3", 1), ("D3", 1)]), 0.44)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D6", "F#6", "A6", "D7", "A6", "D7"]]), 0.4)

    # The engines' drone: a low hum with a slow beat, and a faint high whine. Loops.
    n = int(RATE * 6)
    drone = []
    for i in range(n):
        t = i / RATE
        beat = 0.6 + 0.4 * math.sin(2 * math.pi * t / 1.5) ** 2
        drone.append(beat * (math.sin(2 * math.pi * 49 * t) * 0.6 + math.sin(2 * math.pi * 98.5 * t) * 0.25 + math.sin(2 * math.pi * 147 * t) * 0.1)
                     + 0.05 * math.sin(2 * math.pi * (1320 + 30 * math.sin(2 * math.pi * t / 3)) * t))
    write("Music.wav", drone, 0.26)

    # The title: a driving space march in D minor (new).
    lead = [("D5", 0.5), ("D5", 0.5), ("A5", 1), ("G5", 0.5), ("F5", 0.5), ("E5", 1), ("F5", 0.5), ("G5", 0.5), ("A5", 1), ("D5", 2),
            ("C5", 0.5), ("C5", 0.5), ("G5", 1), ("F5", 0.5), ("E5", 0.5), ("D5", 1), ("E5", 0.5), ("F5", 0.5), ("E5", 1), ("A4", 2),
            ("D5", 0.5), ("D5", 0.5), ("A5", 1), ("Bb5", 0.5), ("A5", 0.5), ("G5", 1), ("F5", 0.5), ("G5", 0.5), ("A5", 1), ("D6", 2),
            ("C6", 0.5), ("Bb5", 0.5), ("A5", 1), ("G5", 0.5), ("E5", 0.5), ("C#5", 1), ("D5", 3), ("-", 1)]
    bass = [("D3", 0.5), ("D3", 0.5)] * 8 + [("C3", 0.5), ("C3", 0.5)] * 4 + [("A2", 0.5), ("A2", 0.5)] * 4 \
         + [("D3", 0.5), ("D3", 0.5)] * 4 + [("Bb2", 0.5), ("Bb2", 0.5)] * 4 + [("G2", 0.5), ("A2", 0.5)] * 4 + [("D3", 4)]
    write("TitleTune.wav", tune(132, lead, bass, kind="pulse"), 0.42)
    write("StartTune.wav", tune(160, [("D5", 0.5), ("F5", 0.5), ("A5", 0.5), ("D6", 0.5), ("C6", 0.5), ("A5", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(76, [("A5", 1), ("F5", 1), ("D5", 1), ("E5", 1), ("C#5", 1), ("D5", 3)], [("D3", 2), ("Bb2", 2), ("A2", 2), ("D3", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
