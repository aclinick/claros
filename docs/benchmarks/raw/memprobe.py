import os, gc
import onnxruntime as rt
import psutil, numpy as np

P = psutil.Process()
def rss(): return P.memory_info().rss/1e6
MODEL = r"D:\source\ttslib-extract\spike-parakeet\model"
enc = os.path.join(MODEL, "encoder-model.int8.onnx")
dec = os.path.join(MODEL, "decoder_joint-model.int8.onnx")
mel = os.path.join(MODEL, "nemo128.onnx")

print(f"baseline               {rss():7.0f} MB")

def opts(arena, pattern):
    o = rt.SessionOptions()
    o.enable_cpu_mem_arena = arena
    o.enable_mem_pattern = pattern
    return o

# --- default options ---
s_mel = rt.InferenceSession(mel, providers=["CPUExecutionProvider"])
print(f"+mel (default)         {rss():7.0f} MB")
s_dec = rt.InferenceSession(dec, providers=["CPUExecutionProvider"])
print(f"+decoder (default)     {rss():7.0f} MB")
s_enc = rt.InferenceSession(enc, providers=["CPUExecutionProvider"])
print(f"+encoder (default)     {rss():7.0f} MB   <-- all loaded, default arena")

del s_mel, s_dec, s_enc; gc.collect()
print(f"after free             {rss():7.0f} MB")

# --- arena + mem_pattern OFF ---
o = opts(False, False)
s_mel = rt.InferenceSession(mel, sess_options=opts(False,False), providers=["CPUExecutionProvider"])
s_dec = rt.InferenceSession(dec, sess_options=opts(False,False), providers=["CPUExecutionProvider"])
s_enc = rt.InferenceSession(enc, sess_options=opts(False,False), providers=["CPUExecutionProvider"])
print(f"+all (arena OFF,patOFF){rss():7.0f} MB   <-- loaded, no arena")

# run one encoder inference to see activation peak
# encoder inputs: audio_signal [1,128,T], length [1]
T = 5804  # ~58s of features at 10ms hop
feats = np.random.randn(1,128,T).astype(np.float32)
lens = np.array([T], dtype=np.int64)
out = s_enc.run(None, {"audio_signal": feats, "length": lens})
print(f"after encoder infer    {rss():7.0f} MB   <-- +activations for 58s")
