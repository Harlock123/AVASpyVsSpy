#!/usr/bin/env python3
"""Synthesises AVASpyVsSpy's sound effects and tunes into ../src/AVASpyVsSpy/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions: a creeping minor walk for the embassy, and a brassier version of it for the title.
Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASpyVsSpy", "Sounds")
RNG = random.Random(1984)
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
    # Searching: a drawer squeaking open.
    write("Search.wav", tone(0.3, 700, 1100, "saw", attack=0.01, vibrato=0.08, vib_rate=30, release=0.6, gain=0.4), 0.14)
    write("Nothing.wav", cat(tone(0.08, hz("E4"), kind="tri", release=0.5), tone(0.14, hz("C4"), kind="tri", release=0.6)), 0.2)
    write("Find.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6"]]), 0.26)
    write("Remedy.wav", cat(tone(0.06, hz("G5"), kind="square", release=0.4), tone(0.1, hz("D6"), kind="square", release=0.5)), 0.22)
    write("AllFive.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "G5", "C6", "E6"]], tone(0.3, hz("G6"), kind="pulse", vibrato=0.02, release=0.6)), 0.3)
    write("SetTrap.wav", cat(tone(0.03, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), silence(0.05), tone(0.04, 900, 600, "square", release=1.0, gain=0.5)), 0.2)
    # The traps going off.
    write("Bomb.wav", mix(tone(1.3, 2400, 60, "noise", attack=0.002, release=0.5), tone(1.0, 120, 30, "sine", release=0.6, gain=0.9)), 0.55)
    write("Spring.wav", tone(0.7, 180, 900, "sine", attack=0.003, vibrato=0.35, vib_rate=14, release=0.7), 0.4)
    write("Pail.wav", cat(tone(0.25, 1400, 400, "sine", release=0.3, gain=0.4), mix(tone(0.15, 2200, 1500, "square", attack=0.001, release=1.2, gain=0.6), tone(0.6, 3000, 500, "noise", release=0.6, gain=0.8))), 0.42)
    write("Gun.wav", mix(tone(0.5, 3000, 200, "noise", attack=0.001, release=1.4), tone(0.2, 220, 60, "square", release=1.2, gain=0.6)), 0.55)
    write("Defuse.wav", cat(tone(0.4, 4000, 1500, "noise", attack=0.01, release=0.6, gain=0.5), tone(0.15, hz("C6"), kind="tri", release=0.5)), 0.3)
    # The clubs.
    write("Swing.wav", tone(0.18, 500, 1400, "noise", attack=0.02, release=0.8, gain=0.5), 0.14)
    write("Hit.wav", mix(tone(0.12, 160, 70, "square", attack=0.001, release=1.0, gain=0.7), tone(0.08, 1800, 400, "noise", release=1.3, gain=0.6)), 0.36)
    write("Die.wav", cat(*[tone(0.09, hz(n), kind="pulse", release=0.4) for n in ["G4", "F#4", "F4", "E4"]], tone(0.5, hz("D#4"), kind="pulse", vibrato=0.04, release=0.6)), 0.32)
    write("Back.wav", tone(0.35, 300, 900, "tri", attack=0.05, vibrato=0.05, vib_rate=10, release=0.6), 0.2)
    write("Door.wav", cat(tone(0.04, 220, 160, "square", attack=0.001, release=1.0, gain=0.5), silence(0.04), tone(0.05, 180, 120, "square", release=1.0, gain=0.5)), 0.16)
    write("Locked.wav", cat(tone(0.1, 140, kind="square", release=0.2), silence(0.04), tone(0.16, 120, kind="square", release=0.4)), 0.22)
    write("Escape.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G4", "C5", "E5", "G5", "C6"]], tone(0.5, 600, 2400, "sine", release=0.6, gain=0.6)), 0.32)
    write("OutOfTime.wav", cat(*[tone(0.2, hz(n), kind="square", release=0.5) for n in ["C5", "G4", "E4"]], tone(0.6, hz("C4"), kind="square", release=0.7)), 0.3)
    write("TimeLow.wav", cat(*[cat(tone(0.04, 1600, kind="square", attack=0.001, release=1.0), silence(0.21)) for _ in range(6)]), 0.2)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)

    # The embassy tune (an original composition): a tiptoeing walking bass in E minor under a sly, chromatic lead,
    # a whole number of bars so it loops.
    walk = [("E2", 0.5), ("G2", 0.5), ("B2", 0.5), ("A#2", 0.5)] * 2 + [("A2", 0.5), ("C3", 0.5), ("E3", 0.5), ("D#3", 0.5)] * 2 + \
           [("E2", 0.5), ("G2", 0.5), ("B2", 0.5), ("A#2", 0.5)] * 2 + [("B2", 0.5), ("D#3", 0.5), ("F#3", 0.5), ("B2", 0.5), ("E2", 0.5), ("B1", 0.5), ("E2", 1)]
    e = 0.5
    lead = [("-", e), ("E4", e), ("F#4", e), ("G4", e), ("B4", e), ("A#4", e), ("A4", e), ("G4", e),
            ("-", e), ("A4", e), ("B4", e), ("C5", e), ("E5", e), ("D#5", e), ("D5", e), ("C5", e),
            ("-", e), ("E4", e), ("F#4", e), ("G4", e), ("E5", e), ("D#5", e), ("E5", e), ("B4", e),
            ("F#5", e), ("D#5", e), ("B4", e), ("A4", e), ("G4", e), ("F#4", e), ("E4", 1)]
    lead, walk = lead * 2, walk * 2
    write("Music.wav", tune(132, lead, walk, kind="pulse"), 0.26)
    write("TitleTune.wav", cat(tune(116, lead, walk, kind="square"), silence(0.8)), 0.34)
    write("Victory.wav", tune(150, [("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D#6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G6", 3)],
                              [("E3", 1), ("B2", 1), ("C3", 1), ("A2", 1), ("B2", 1), ("E3", 2.5)], kind="pulse"), 0.4)
    write("Lost.wav", tune(90, [("B4", 1), ("A#4", 1), ("A4", 1), ("G#4", 1), ("G4", 3)], [("E3", 2), ("D#3", 2), ("E2", 3)], kind="tri"), 0.4)
    write("GameOver.wav", tune(66, [("G4", 1), ("E4", 1), ("D#4", 1), ("B3", 1), ("E4", 4)], [("E3", 2), ("C3", 2), ("B2", 2), ("E2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
