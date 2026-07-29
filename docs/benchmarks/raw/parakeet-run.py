import sys, time, os
import onnx_asr

MODEL_DIR = r"D:\source\ttslib-extract\spike-parakeet\model"
wavs = sys.argv[1:] or [
    r"D:\source\ttslib-extract\spike-parakeet\call_mono.wav",
    r"D:\source\ttslib-extract\spike-parakeet\call_left.wav",
    r"D:\source\ttslib-extract\spike-parakeet\call_right.wav",
]

t0 = time.time()
model = onnx_asr.load_model("nemo-conformer-tdt", MODEL_DIR, quantization="int8")
print(f"[load] {time.time()-t0:.2f}s", flush=True)

for w in wavs:
    if not os.path.exists(w):
        print(f"[skip] {w} missing"); continue
    t1 = time.time()
    text = model.recognize(w)
    dt = time.time() - t1
    print("\n" + "=" * 70)
    print(f"FILE: {os.path.basename(w)}   ({dt:.2f}s)")
    print("=" * 70)
    print(text)

try:
    import psutil
    rss = psutil.Process().memory_info().rss / 1e6
    print(f"\n[peak RSS] {rss:.0f} MB")
except Exception:
    pass
