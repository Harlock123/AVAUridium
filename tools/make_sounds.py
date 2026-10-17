#!/usr/bin/env python3
"""Synthesises AVAUridium's sound effects and tunes into ../src/AVAUridium/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions. The dreadnought alarm's voice comes from the formant voice in speech.py, the way the
talking arcade games of the day spoke, so no recording or speech engine is needed. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAUridium", "Sounds")
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


def echoed(dry, taps=((0.03, 0.15),)):
    """A little ring of the loudspeaker."""
    out = dry + [0.0] * int(RATE * 0.3)
    for delay, gain in taps:
        k = int(RATE * delay)
        for i, v in enumerate(dry):
            out[i + k] += v * gain
    return out


def computer(words):
    """The Shrike's computer: a clear, level voice with a fifth humming under it, a little metallic."""
    voice = say(words, rate=RATE, pitch=142, speed=1.05, size=0.9)
    hum = say(words, rate=RATE, pitch=213, speed=1.05, size=0.9, seed=11)
    return echoed([a + 0.15 * b for a, b in zip(voice, hum)])


# The computer's lines, written out as phonemes.
LINES = {
    "SayDread": "D R EH1 D N AO T , AH HH EH1 D !",
    "SayLand": "L AE1 N D , N AA1 UW !",
    "SayDestroyed": "D R EH1 D N AO T , D IH S T R AO1 IY D !",
}


def arp(chord, beats, step=0.25):
    """A rippling arpeggio over a chord, for the given beats."""
    notes = []
    n = int(beats / step)
    for i in range(n):
        notes.append((chord[i % len(chord)], step))
    return notes


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The Shrike.
    write("Fire.wav", tone(0.08, 2600, 1100, "square", attack=0.001, release=1.4, gain=0.5), 0.14)
    write("Loop.wav", tone(0.5, 300, 900, "tri", attack=0.01, vibrato=0.04, vib_rate=10, release=0.6), 0.2)
    write("Die.wav", mix(tone(1.4, 2600, 60, "noise", attack=0.001, release=0.4), tone(1.2, 220, 30, "saw", vibrato=0.25, vib_rate=10, release=0.5, gain=0.6)), 0.5)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["E5", "G#5", "B5", "E6", "B5", "E6"]]), 0.3)
    # Hits.
    write("Boom.wav", mix(tone(0.3, 2000, 200, "noise", attack=0.001, release=0.9), tone(0.25, 300, 70, "square", release=0.8, gain=0.5)), 0.3)
    write("BigBoom.wav", mix(tone(0.7, 2400, 90, "noise", attack=0.001, release=0.6), tone(0.6, 160, 35, "saw", vibrato=0.2, vib_rate=12, release=0.6, gain=0.6)), 0.42)
    write("Hit.wav", tone(0.05, 1300, 900, "square", attack=0.001, release=1.2, gain=0.5), 0.18)
    write("PodDie.wav", cat(mix(tone(0.5, 2200, 120, "noise", attack=0.001, release=0.6), tone(0.4, 600, 100, "tri", release=0.6, gain=0.6)),
                           *[tone(0.05, hz(n), kind="pulse", release=0.4) for n in ["E6", "B5", "G5"]]), 0.4)
    # The defences.
    write("WaveIn.wav", tone(0.35, 400, 900, "saw", attack=0.02, vibrato=0.1, vib_rate=14, release=0.6), 0.14)
    write("WaveBonus.wav", cat(*[tone(0.05, hz(n), kind="square", release=0.3) for n in ["E5", "B5", "E6", "G#6", "B6"]]), 0.26)
    write("EnemyFire.wav", tone(0.1, 700, 1100, "square", attack=0.001, release=1.0, gain=0.4), 0.12)
    write("MineOut.wav", tone(0.3, 200, 500, "tri", attack=0.01, vibrato=0.2, vib_rate=20, release=0.6), 0.22)
    # Landing, the reactor, and the end of a dreadnought.
    write("LandNow.wav", cat(*[cat(tone(0.18, 880, kind="square", release=0.2, gain=0.5), tone(0.18, 660, kind="square", release=0.2, gain=0.5)) for _ in range(3)]), 0.26)
    write("Land.wav", cat(tone(0.4, 900, 200, "noise", attack=0.01, release=0.8, gain=0.5), tone(0.2, 120, 80, "sine", release=0.8)), 0.3)
    write("Tick.wav", tone(0.03, 1800, kind="square", attack=0.001, release=1.0, gain=0.5), 0.12)
    write("Stop.wav", cat(tone(0.08, hz("E6"), kind="pulse", release=0.3), tone(0.25, hz("B6"), kind="pulse", vibrato=0.01, release=0.5)), 0.3)
    write("Destruct.wav", cat(*[mix(tone(0.4, 2400 - i * 250, 120, "noise", attack=0.001, release=0.7), tone(0.35, 220 - i * 20, 40, "saw", release=0.7, gain=0.5)) for i in range(6)],
                             mix(tone(2.0, 2600, 40, "noise", attack=0.002, release=0.35), tone(1.8, 110, 25, "saw", vibrato=0.3, vib_rate=8, release=0.4, gain=0.7))), 0.55)
    # The computer.
    for name, words in LINES.items():
        write(f"{name}.wav", computer(words), 0.75)

    # The flying tune (an original composition): a bright line in E minor over rippling arpeggios, looping.
    em, c, g, d, am, b = ["E3", "G3", "B3", "E4"], ["C3", "E3", "G3", "C4"], ["G2", "B2", "D3", "G3"], ["D3", "F#3", "A3", "D4"], ["A2", "C3", "E3", "A3"], ["B2", "D#3", "F#3", "B3"]
    lead = [("E5", 1), ("G5", 0.5), ("B5", 1), ("A5", 0.5), ("G5", 0.5), ("F#5", 0.5),
            ("E5", 1), ("C5", 0.5), ("E5", 1.5), ("G5", 1),
            ("D5", 1), ("G5", 0.5), ("B5", 1), ("D6", 0.5), ("C6", 0.5), ("B5", 0.5),
            ("A5", 1), ("F#5", 0.5), ("D5", 1.5), ("F#5", 1),
            ("E5", 1), ("G5", 0.5), ("B5", 1), ("E6", 0.5), ("D6", 0.5), ("B5", 0.5),
            ("C6", 1), ("A5", 0.5), ("E5", 1.5), ("A5", 1),
            ("G5", 1), ("F#5", 0.5), ("E5", 1), ("D#5", 0.5), ("F#5", 0.5), ("B5", 0.5),
            ("E5", 3), ("-", 1)]
    harmony = arp(em, 4) + arp(c, 4) + arp(g, 4) + arp(d, 4) + arp(em, 4) + arp(am, 4) + arp(b, 4) + arp(em, 4)
    write("Music.wav", tune(150, lead, harmony, kind="pulse"), 0.26)
    title_lead = [("B4", 1), ("E5", 1), ("G5", 1), ("F#5", 0.5), ("E5", 0.5), ("D5", 1), ("E5", 1), ("B4", 2),
                  ("C5", 1), ("E5", 1), ("A5", 1), ("G5", 0.5), ("F#5", 0.5), ("D#5", 2), ("E5", 2), ("-", 2)]
    title_bass = arp(["E2", "B2", "E3", "G3"], 4, 0.5) + arp(["D2", "A2", "D3", "F#3"], 4, 0.5) + arp(["A1", "E2", "A2", "C3"], 4, 0.5) + arp(["B1", "F#2", "B2", "D#3"], 2, 0.5) + arp(["E2", "B2", "E3", "G3"], 4, 0.5)
    write("TitleTune.wav", cat(tune(112, title_lead, title_bass, kind="pulse"), silence(0.8)), 0.38)
    write("Victory.wav", tune(120, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D#6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G#6", 3)],
                              [("E2", 1), ("B1", 1), ("C#2", 1), ("A1", 1), ("B1", 1), ("E2", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E2", 2), ("C2", 2), ("B1", 2), ("E1", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
