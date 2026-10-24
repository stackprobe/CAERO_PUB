"""Render original 5-minute scores to MP3. Runtime needs only the resulting MP3s.

Requires Python + numpy and lameenc, or an explicitly supplied LAME shared library (not redistributed).
Example: python Tools/compose_ambient.py --lame path/to/libmp3lame64.dll
"""
import argparse
import ctypes as ct
import hashlib
import json
from pathlib import Path
import wave

import numpy as np

RATE, SECONDS = 44100, 300
ROOT = Path(__file__).resolve().parents[1]


def smooth(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def compose(night):
    """Nine slow harmonic scenes, soft keys, and changing stereo pad orchestration."""
    rng = np.random.default_rng(8942 if night else 3207)
    pcm = np.zeros((RATE * SECONDS, 2), dtype=np.float32)
    chords = (
        [[38, 50, 57, 60, 64, 69], [34, 46, 53, 57, 60, 65], [31, 43, 50, 57, 62, 65],
         [33, 45, 52, 55, 59, 64], [38, 50, 55, 60, 65, 69], [36, 48, 55, 59, 62, 67],
         [34, 46, 53, 57, 62, 65], [33, 45, 52, 57, 62, 64], [38, 50, 57, 62, 65, 69]]
        if night else
        [[38, 50, 57, 61, 64, 69], [35, 47, 54, 57, 61, 66], [31, 43, 50, 54, 57, 62],
         [33, 45, 52, 57, 59, 64], [40, 52, 55, 59, 62, 66], [42, 50, 57, 61, 64, 69],
         [31, 43, 50, 57, 62, 66], [33, 45, 52, 57, 62, 64], [38, 50, 57, 62, 64, 69]]
    )

    def note(start, duration, midi, amplitude, pan, instrument, phase=0):
        first = int(start * RATE)
        count = min(int(duration * RATE), len(pcm) - first)
        if count <= 0:
            return
        t = np.arange(count, dtype=np.float64) / RATE
        frequency = 440 * 2 ** ((midi - 69) / 12)
        if instrument == "pad":
            envelope = smooth(t / 7) * smooth((duration - t) / 10)
            tone = np.zeros(count)
            for detune, spread in [(0.9986, .3), (1.0014, 1.7)]:
                vibrato = .12 * np.sin(2 * np.pi * .07 * t + phase + spread)
                p = 2 * np.pi * frequency * detune * t + phase + vibrato
                tone += np.sin(p) + (.10 if night else .18) * np.sin(p * 2 + .3) + .055 * np.sin(p * 3)
            tone *= .5 * (.85 + .15 * np.sin(2 * np.pi * .031 * t + phase))
        elif instrument == "felt":
            envelope = smooth(t / .08) * np.exp(-t / (4.5 if night else 3.2)) * smooth((duration - t) / 2)
            p = 2 * np.pi * frequency * t
            tone = np.sin(p) + .18 * np.sin(p * 2) * np.exp(-t) + .07 * np.sin(p * 3.003) * np.exp(-t * 1.5)
        else:  # Sparse soft glass overtones, without percussion.
            envelope = smooth(t / .7) * np.exp(-t / 4) * smooth((duration - t) / 2)
            p = 2 * np.pi * frequency * t
            tone = np.sin(p) + .23 * np.sin(p * 2.001) * np.exp(-t * .5)
        signal = amplitude * envelope * tone
        pcm[first:first + count, 0] += (signal * np.sqrt(1 - pan)).astype(np.float32)
        pcm[first:first + count, 1] += (signal * np.sqrt(pan)).astype(np.float32)

    for scene, chord in enumerate(chords):
        start = 2 + scene * 32
        for voice, midi in enumerate(chord):
            note(start + voice * .3, 42, midi, .038 if night else .043,
                 .15 + .7 * ((voice * .37 + scene * .13) % 1), "pad", rng.uniform(0, 6.28))
        melody = ([chord[4] + 12, chord[3] + 12, chord[2] + 12, chord[4] + 12]
                  if night else [chord[3] + 12, chord[4] + 12, chord[5] + 12, chord[2] + 12])
        for step, midi in enumerate(melody):
            onset = start + 6 + step * (7.3 if night else 6.5) + rng.uniform(-.5, .5)
            note(onset, 15, midi, .09 if night else .105, .25 + rng.random() * .5, "felt")
        if scene % 2 == (1 if night else 0):
            note(start + 19, 17, chord[-2] + 24, .025 if night else .04, .2 + rng.random() * .6, "glass")

    # Diffuse stereo echoes; a global ending fade silences even the reverb tails.
    dry = pcm.copy()
    for delay, gain in [(0.31, .17), (0.73, .13), (1.31, .11), (2.17, .08), (3.19, .05)]:
        frames = int(delay * RATE)
        pcm[frames:] += dry[:-frames, ::-1] * gain
    del dry
    t = np.arange(len(pcm), dtype=np.float64) / RATE
    envelope = smooth((t - 1.5) / 12) * smooth((298 - t) / 22)
    pcm *= envelope[:, None].astype(np.float32)
    pcm *= .48 / max(float(np.max(np.abs(pcm))), .001)
    # Explicit silence survives MP3 delay/padding and transform-window ringing.
    pcm[:int(1.5 * RATE)] = 0
    pcm[int(298 * RATE):] = 0
    assert np.max(np.abs(pcm[:RATE])) == 0 and np.max(np.abs(pcm[-RATE:])) == 0
    return pcm


def encode_mp3(pcm, destination, library):
    if library is None:
        import lameenc
        encoder = lameenc.Encoder()
        encoder.set_bit_rate(192)
        encoder.set_in_sample_rate(RATE)
        encoder.set_channels(2)
        encoder.set_quality(2)
        with destination.open("wb") as output:
            for first in range(0, len(pcm), 16384):
                output.write(encoder.encode(np.rint(pcm[first:first + 16384] * 32767).astype("<i2").tobytes()))
            output.write(encoder.flush())
        return
    lame = ct.CDLL(str(library))
    lame.lame_init.restype = ct.c_void_p
    handle = lame.lame_init()
    if not handle:
        raise RuntimeError("LAME initialization failed")
    for name in ["lame_set_in_samplerate", "lame_set_out_samplerate", "lame_set_num_channels",
                 "lame_set_brate", "lame_set_quality", "lame_set_mode", "lame_set_num_samples"]:
        getattr(lame, name).argtypes = [ct.c_void_p, ct.c_int if name != "lame_set_num_samples" else ct.c_ulong]
    lame.lame_init_params.argtypes = [ct.c_void_p]
    lame.lame_encode_buffer_interleaved_ieee_float.argtypes = [ct.c_void_p, ct.POINTER(ct.c_float), ct.c_int, ct.c_void_p, ct.c_int]
    lame.lame_encode_flush.argtypes = [ct.c_void_p, ct.c_void_p, ct.c_int]
    lame.lame_get_lametag_frame.argtypes = [ct.c_void_p, ct.c_void_p, ct.c_size_t]
    lame.lame_get_lametag_frame.restype = ct.c_size_t
    lame.lame_close.argtypes = [ct.c_void_p]
    try:
        for name, value in [("in_samplerate", RATE), ("out_samplerate", RATE), ("num_channels", 2),
                            ("brate", 192), ("quality", 2), ("mode", 1), ("num_samples", len(pcm))]:
            if getattr(lame, "lame_set_" + name)(handle, value) < 0:
                raise RuntimeError("Invalid encoder setting: " + name)
        if lame.lame_init_params(handle) < 0:
            raise RuntimeError("LAME parameter initialization failed")
        buffer = ct.create_string_buffer(100000)
        with destination.open("wb+") as output:
            for first in range(0, len(pcm), 16384):
                chunk = np.ascontiguousarray(pcm[first:first + 16384])
                count = lame.lame_encode_buffer_interleaved_ieee_float(handle, chunk.ctypes.data_as(ct.POINTER(ct.c_float)), len(chunk), buffer, len(buffer))
                if count < 0:
                    raise RuntimeError("MP3 encoding failed: " + str(count))
                output.write(buffer.raw[:count])
            count = lame.lame_encode_flush(handle, buffer, len(buffer))
            if count < 0:
                raise RuntimeError("MP3 encoder flush failed")
            output.write(buffer.raw[:count])
            count = lame.lame_get_lametag_frame(handle, buffer, len(buffer))
            if count > len(buffer):
                raise RuntimeError("LAME tag exceeds buffer")
            if count:
                output.seek(0)
                output.write(buffer.raw[:count])
    finally:
        lame.lame_close(handle)


def compose_title():
    """Stillness Before the Threshold: suspended, slowly breathing harmony, no attacks or beat."""
    rng = np.random.default_rng(170124)
    pcm = np.zeros((RATE * SECONDS, 2), dtype=np.float32)
    # An open D/A axis returns at the end, without a cadence or a foreground melody.
    chords = [[38, 50, 57, 64, 69], [38, 50, 59, 64, 69], [38, 52, 57, 62, 69],
              [43, 50, 57, 62, 66], [43, 52, 57, 62, 69], [38, 50, 57, 64, 69]]
    for scene, chord in enumerate(chords):
        for voice, midi in enumerate(chord):
            start, duration = 2 + scene * 46 + voice * .73, 65
            first = int(start * RATE)
            count = min(int(duration * RATE), len(pcm) - first)
            t = np.arange(count, dtype=np.float64) / RATE
            f = 440 * 2 ** ((midi - 69) / 12)
            phase = rng.uniform(0, 2 * np.pi)
            envelope = smooth(t / 18) * smooth((duration - t) / 26)
            breath = .88 + .12 * np.sin(2 * np.pi * .018 * t + phase)
            for channel, detune in enumerate([.9996, 1.0004]):
                p = 2 * np.pi * f * detune * t + phase + .09 * np.sin(t * .19 + channel)
                tone = np.sin(p) + .045 * np.sin(2 * p + .4) + .012 * np.sin(3 * p)
                pan = .5 + .18 * np.sin(voice * 1.9 + scene * .7 + channel * np.pi)
                pcm[first:first + count, channel] += (tone * envelope * breath * .027 * np.sqrt(pan)).astype(np.float32)
    dry = pcm.copy()
    for delay, gain in [(1.13, .20), (2.71, .16), (4.37, .12), (7.19, .08), (10.31, .045)]:
        frames = int(delay * RATE)
        pcm[frames:] += dry[:-frames, ::-1] * gain
    del dry
    t = np.arange(len(pcm), dtype=np.float64) / RATE
    pcm *= (smooth((t - 1.5) / 22) * smooth((298 - t) / 36))[:, None].astype(np.float32)
    pcm *= .22 / max(float(np.max(np.abs(pcm))), .001)
    pcm[:int(1.5 * RATE)] = 0
    pcm[int(298 * RATE):] = 0
    assert np.max(np.abs(pcm[:RATE])) == 0 and np.max(np.abs(pcm[-RATE:])) == 0
    return pcm


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lame", type=Path, help="LAME shared library; otherwise use the lameenc Python package")
    parser.add_argument("--track", choices=["day", "night", "title", "all"], default="all")
    args = parser.parse_args()
    target = ROOT / "CAERO" / "Music"
    preview = ROOT / "SaveData" / "Audio" / "Composition"
    target.mkdir(parents=True, exist_ok=True)
    preview.mkdir(parents=True, exist_ok=True)
    manifest_path = target / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else []
    for name, title in [("day", "Drifting Corridors"), ("night", "The Unlit Courtyard"), ("title", "Stillness Before the Threshold")]:
        if args.track not in ("all", name):
            continue
        print("Composing " + name, flush=True)
        pcm = compose_title() if name == "title" else compose(name == "night")
        path = target / (name + ".mp3")
        encode_mp3(pcm, path, args.lame)
        # Lossless master is a local QA artifact, not a runtime dependency.
        with wave.open(str(preview / (name + "-master.wav")), "wb") as wav:
            wav.setparams((2, 2, RATE, 0, "NONE", "not compressed"))
            wav.writeframes(np.rint(pcm * 32767).astype("<i2").tobytes())
        info = {"name": name, "title": title, "seconds": SECONDS, "rate": RATE, "channels": 2,
                "bitrate_kbps": 192, "peak": float(np.max(np.abs(pcm))),
                "rms": float(np.sqrt(np.mean(pcm * pcm))), "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
        manifest = [entry for entry in manifest if entry["name"] != name] + [info]
        print(json.dumps(info), flush=True)
        del pcm
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
