#!/usr/bin/env python3
"""Rebuilds the recorded catch and finish sounds from their sources (TASK-0022).

The takes come from one CC0 pack on Freesound, "Water Paddle" by EpicWizard: single paddle
strokes in a lake, recorded one at a time. Each is cut to the part the app needs and
written as 48 kHz mono WAV under Assets/Resources/Audio/Stroke/, where StrokeAudio loads
them. Assets/Resources/Audio/Stroke/SOURCES.md records where each file came from.

Freesound only serves the original WAVs to logged-in users, so this uses the public
high-quality MP3 previews. Needs ffmpeg and network access. The output is committed
(Git LFS), so this only needs running to change the selection or the cut.

    tools/stroke-sounds.py
"""
import array
import math
import pathlib
import subprocess
import urllib.request

RATE = 48000
OUT = pathlib.Path(__file__).resolve().parent.parent / "Assets/Resources/Audio/Stroke"
PREVIEW = "https://cdn.freesound.org/previews/316/{id}_2291325-hq.mp3"

# (output name, Freesound id, seconds kept from the onset).
# Catch: medium and heavy strokes that hit hard straight away, cut to the entry and splash.
# Finish: soft strokes, which are a slosh and drips with no hard entry, cut before the
# paddle's second splash so each is a single event.
TAKES = [
    ("Catch/catch-1", 316593, 0.8),   # Water_Paddle_med_010
    ("Catch/catch-2", 316576, 0.8),   # Water_Paddle_med_001
    ("Catch/catch-3", 316589, 0.8),   # Water_Paddle_med_004
    ("Catch/catch-4", 316596, 0.8),   # Water_Paddle_med_013
    ("Catch/catch-5", 316580, 0.8),   # Water_Paddle_med_018
    ("Catch/catch-6", 316569, 0.8),   # Water_Paddle_heavy_026
    ("Finish/finish-1", 316584, 0.5),  # Water_Paddle_soft_020
    ("Finish/finish-2", 316577, 0.5),  # Water_Paddle_soft_021
    ("Finish/finish-3", 316598, 0.5),  # Water_Paddle_soft_023
    ("Finish/finish-4", 316597, 0.5),  # Water_Paddle_soft_024
    ("Finish/finish-5", 316578, 0.5),  # Water_Paddle_soft_022
]

LEAD = 0.005      # kept before the onset, so the attack is not clipped
FADE_IN = 0.003
FADE_OUT = 0.4    # of the take's length
PEAK = 0.9


def decode(sound_id):
    request = urllib.request.Request(PREVIEW.format(id=sound_id), headers={"User-Agent": "Mozilla/5.0"})
    mp3 = urllib.request.urlopen(request).read()
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", "pipe:0", "-ac", "1", "-ar", str(RATE), "-f", "s16le", "pipe:1"],
        input=mp3, stdout=subprocess.PIPE, check=True).stdout
    samples = array.array("h")
    samples.frombytes(raw)
    return [s / 32768.0 for s in samples]


def onset(samples):
    """First sample whose 10 ms level reaches a tenth of the loudest 10 ms."""
    window = RATE // 100
    levels = [max(abs(s) for s in samples[i:i + window]) for i in range(0, len(samples), window)]
    threshold = max(levels) * 0.1
    return next(i for i, level in enumerate(levels) if level >= threshold) * window


def cut(samples, seconds):
    start = max(0, onset(samples) - int(LEAD * RATE))
    take = samples[start:start + int(seconds * RATE)]
    fade_in, fade_out = int(FADE_IN * RATE), int(FADE_OUT * len(take))
    for i in range(min(fade_in, len(take))):
        take[i] *= i / fade_in
    for i in range(fade_out):
        take[len(take) - 1 - i] *= 0.5 - 0.5 * math.cos(math.pi * i / fade_out)
    gain = PEAK / max(abs(s) for s in take)
    return [s * gain for s in take]


def write(path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = array.array("h", (int(round(s * 32767)) for s in samples)).tobytes()
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-f", "s16le", "-ar", str(RATE), "-ac", "1", "-i", "pipe:0",
         "-bitexact", "-c:a", "pcm_s16le", str(path)],
        input=pcm, check=True)


for name, sound_id, seconds in TAKES:
    take = cut(decode(sound_id), seconds)
    write(OUT / (name + ".wav"), take)
    print(f"{name}.wav  {len(take) / RATE:.2f} s  from freesound.org/s/{sound_id}")
