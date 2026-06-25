import argparse
import queue
import sys
import time

import numpy as np
import sounddevice as sd
from openwakeword.model import Model


def parse_models(value):
    return [item.strip() for item in value.replace("|", ",").split(",") if item.strip()]


def main():
    parser = argparse.ArgumentParser(description="OpenWakeWord microphone listener")
    parser.add_argument("--models", default="hey_jarvis")
    parser.add_argument("--threshold", type=float, default=0.5)
    parser.add_argument("--cooldown-ms", type=int, default=4000)
    parser.add_argument("--device", default="")
    parser.add_argument("--vad-threshold", type=float, default=0.0)
    parser.add_argument("--log-scores", action="store_true")
    parser.add_argument("--list-devices", action="store_true")
    args = parser.parse_args()

    if args.list_devices:
        print(sd.query_devices(), flush=True)
        return 0

    requested_models = parse_models(args.models)
    models = list(requested_models)
    audio_queue = queue.Queue()
    last_wake = 0.0

    model = Model(
        wakeword_models=models,
        inference_framework="onnx",
        vad_threshold=args.vad_threshold,
    )

    def callback(indata, frames, callback_time, status):
        if status:
            print(f"AUDIO_STATUS {status}", flush=True)
        audio_queue.put(indata.copy())

    device = int(args.device) if str(args.device).strip().isdigit() else None
    print(
        "READY models="
        + ",".join(requested_models)
        + f" threshold={args.threshold} cooldown_ms={args.cooldown_ms}",
        flush=True,
    )

    with sd.InputStream(
        samplerate=16000,
        channels=1,
        dtype="int16",
        blocksize=1280,
        device=device,
        callback=callback,
    ):
        while True:
            frame = audio_queue.get()
            audio = np.asarray(frame).reshape(-1)
            predictions = model.predict(audio)
            now = time.monotonic()

            best_name = ""
            best_score = 0.0
            for name, score in predictions.items():
                if score > best_score:
                    best_name = name
                    best_score = float(score)

            if args.log_scores and best_name:
                print(f"SCORE {best_name} {best_score:.3f}", flush=True)

            if best_name and best_score >= args.threshold:
                if (now - last_wake) * 1000 >= args.cooldown_ms:
                    last_wake = now
                    print(f"WAKE {best_name} {best_score:.3f}", flush=True)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(0)
    except Exception as exc:
        print(f"ERROR {type(exc).__name__}: {exc}", file=sys.stderr, flush=True)
        raise
