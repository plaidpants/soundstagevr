// SoundStage: low-latency microphone input on Android through AAudio (2026-11-26).
//
// Unity's Microphone on the Quest runs at 16 kHz through OpenSL ES and hands audio over
// in 16-64 ms chunks, which C# then had to play through an AudioSource just to get it
// into the audio thread. This opens the microphone with AAudio in low-latency mode: its
// callback writes into a ring buffer, and each Microphone device's processBuffer reads
// straight out of it on Unity's audio thread (resampled to the output rate, with a tiny,
// inaudible rate trim to follow the drift between the two clocks).
//
// Compiled by IL2CPP as part of the Android build (a source plugin: no separate .so);
// called from C# with [DllImport("__Internal")] (MicNative in MicrophoneSignalGenerator).
// AAudio is loaded with dlopen, so nothing extra has to be linked.

#ifdef __ANDROID__

#include <dlfcn.h>
#include <atomic>
#include <stdint.h>
#include <string.h>
#include <math.h>

typedef struct AAudioStreamStruct AAudioStream;
typedef struct AAudioStreamBuilderStruct AAudioStreamBuilder;
typedef int32_t aaudio_result_t;
typedef int32_t (*AADataCb)(AAudioStream*, void*, void*, int32_t);
typedef void (*AAErrCb)(AAudioStream*, void*, aaudio_result_t);

enum {
  AA_DIRECTION_INPUT = 1,
  AA_FORMAT_PCM_FLOAT = 2,
  AA_SHARING_EXCLUSIVE = 0,
  AA_PERFORMANCE_LOW_LATENCY = 12,
  AA_CALLBACK_CONTINUE = 0,
};

static struct {
  bool loaded, tried;
  aaudio_result_t (*createStreamBuilder)(AAudioStreamBuilder**);
  void (*setDirection)(AAudioStreamBuilder*, int32_t);
  void (*setSharingMode)(AAudioStreamBuilder*, int32_t);
  void (*setPerformanceMode)(AAudioStreamBuilder*, int32_t);
  void (*setFormat)(AAudioStreamBuilder*, int32_t);
  void (*setChannelCount)(AAudioStreamBuilder*, int32_t);
  void (*setSampleRate)(AAudioStreamBuilder*, int32_t);
  void (*setInputPreset)(AAudioStreamBuilder*, int32_t);
  void (*setDataCallback)(AAudioStreamBuilder*, AADataCb, void*);
  void (*setErrorCallback)(AAudioStreamBuilder*, AAErrCb, void*);
  aaudio_result_t (*openStream)(AAudioStreamBuilder*, AAudioStream**);
  aaudio_result_t (*builderDelete)(AAudioStreamBuilder*);
  aaudio_result_t (*requestStart)(AAudioStream*);
  aaudio_result_t (*requestStop)(AAudioStream*);
  aaudio_result_t (*close)(AAudioStream*);
  int32_t (*getSampleRate)(AAudioStream*);
  int32_t (*getFramesPerBurst)(AAudioStream*);
  int32_t (*getBufferSizeInFrames)(AAudioStream*);
  int32_t (*getPerformanceMode)(AAudioStream*);
  int32_t (*getSharingMode)(AAudioStream*);
  int32_t (*getChannelCount)(AAudioStream*);
  int32_t (*getXRunCount)(AAudioStream*);
  const char* (*convertResultToText)(aaudio_result_t);
} A;

template <typename T> static bool Sym(void* lib, T& f, const char* name) {
  f = (T)dlsym(lib, name);
  return f != nullptr;
}

static bool Load() {
  if (A.tried) return A.loaded;
  A.tried = true;
  void* lib = dlopen("libaaudio.so", RTLD_NOW);
  if (!lib) return false;
  bool ok = true;
  ok &= Sym(lib, A.createStreamBuilder, "AAudio_createStreamBuilder");
  ok &= Sym(lib, A.setDirection, "AAudioStreamBuilder_setDirection");
  ok &= Sym(lib, A.setSharingMode, "AAudioStreamBuilder_setSharingMode");
  ok &= Sym(lib, A.setPerformanceMode, "AAudioStreamBuilder_setPerformanceMode");
  ok &= Sym(lib, A.setFormat, "AAudioStreamBuilder_setFormat");
  ok &= Sym(lib, A.setChannelCount, "AAudioStreamBuilder_setChannelCount");
  ok &= Sym(lib, A.setSampleRate, "AAudioStreamBuilder_setSampleRate");
  ok &= Sym(lib, A.setDataCallback, "AAudioStreamBuilder_setDataCallback");
  ok &= Sym(lib, A.setErrorCallback, "AAudioStreamBuilder_setErrorCallback");
  ok &= Sym(lib, A.openStream, "AAudioStreamBuilder_openStream");
  ok &= Sym(lib, A.builderDelete, "AAudioStreamBuilder_delete");
  ok &= Sym(lib, A.requestStart, "AAudioStream_requestStart");
  ok &= Sym(lib, A.requestStop, "AAudioStream_requestStop");
  ok &= Sym(lib, A.close, "AAudioStream_close");
  ok &= Sym(lib, A.getSampleRate, "AAudioStream_getSampleRate");
  ok &= Sym(lib, A.getFramesPerBurst, "AAudioStream_getFramesPerBurst");
  ok &= Sym(lib, A.getBufferSizeInFrames, "AAudioStream_getBufferSizeInFrames");
  ok &= Sym(lib, A.getPerformanceMode, "AAudioStream_getPerformanceMode");
  ok &= Sym(lib, A.getSharingMode, "AAudioStream_getSharingMode");
  ok &= Sym(lib, A.getChannelCount, "AAudioStream_getChannelCount");
  ok &= Sym(lib, A.getXRunCount, "AAudioStream_getXRunCount");
  Sym(lib, A.setInputPreset, "AAudioStreamBuilder_setInputPreset"); // API 28+
  Sym(lib, A.convertResultToText, "AAudio_convertResultToText");
  A.loaded = ok;
  return ok;
}

// ---- the ring: mono, at the microphone's rate; one writer (the AAudio callback) ----
static const int RING = 1 << 15;          // 32768 frames (~0.7 s at 48 kHz)
static float ring[RING];
static std::atomic<int64_t> written(0);   // frames written, ever
static std::atomic<int> inRate(0), channels(1), burst(0);
static std::atomic<bool> running(false), failed(false);
static std::atomic<int> callbacks(0), peakMilli(0);
static AAudioStream* stream = nullptr;

static int32_t OnData(AAudioStream*, void*, void* data, int32_t frames) {
  const float* in = (const float*)data;
  int ch = channels.load(std::memory_order_relaxed);
  int64_t w = written.load(std::memory_order_relaxed);
  float peak = 0;
  for (int i = 0; i < frames; i++) {
    float v = in[i * ch];
    ring[(w + i) & (RING - 1)] = v;
    float a = fabsf(v);
    if (a > peak) peak = a;
  }
  written.store(w + frames, std::memory_order_release);
  callbacks.fetch_add(1, std::memory_order_relaxed);
  int pm = (int)(peak * 1000);
  if (pm > peakMilli.load(std::memory_order_relaxed)) peakMilli.store(pm, std::memory_order_relaxed);
  return AA_CALLBACK_CONTINUE;
}

static void OnError(AAudioStream*, void*, aaudio_result_t) {
  failed.store(true); // C# sees it and reopens (a stream can't be closed from here)
}

// ---- readers: each Microphone device reads the ring on its own ----
static const int READERS = 8;
struct Reader {
  std::atomic<bool> used;
  bool primed;
  double pos;          // read position in input frames
  int outRate;
  float targetMs;
  std::atomic<int> underruns, drops, fillMin, fillMax, reads;
  float trim;          // current rate trim (for the stats)
};
static Reader readers[READERS];

extern "C" {

// open the microphone: preset = AAudio input preset (6 voice recognition, 9 unprocessed,
// 10 voice performance, 1 generic, 5 camcorder); returns the rate (> 0) or an AAudio
// error (< 0); -1000 = AAudio isn't there
int SSMic_Start(int preset, int exclusive) {
  if (!Load()) return -1000;
  if (stream) return inRate.load();
  AAudioStreamBuilder* b = nullptr;
  aaudio_result_t r = A.createStreamBuilder(&b);
  if (r != 0) return r;
  A.setDirection(b, AA_DIRECTION_INPUT);
  A.setPerformanceMode(b, AA_PERFORMANCE_LOW_LATENCY);
  if (exclusive) A.setSharingMode(b, AA_SHARING_EXCLUSIVE); // falls back to shared by itself
  A.setFormat(b, AA_FORMAT_PCM_FLOAT);
  A.setChannelCount(b, 1);
  if (preset > 0 && A.setInputPreset) A.setInputPreset(b, preset);
  A.setDataCallback(b, OnData, nullptr);
  A.setErrorCallback(b, OnError, nullptr);
  r = A.openStream(b, &stream);
  A.builderDelete(b);
  if (r != 0) { stream = nullptr; return r; }
  inRate.store(A.getSampleRate(stream));
  channels.store(A.getChannelCount(stream) > 0 ? A.getChannelCount(stream) : 1);
  burst.store(A.getFramesPerBurst(stream));
  written.store(0);
  failed.store(false);
  for (int i = 0; i < READERS; i++) readers[i].primed = false;
  r = A.requestStart(stream);
  if (r != 0) { A.close(stream); stream = nullptr; return r; }
  running.store(true);
  return inRate.load();
}

void SSMic_Stop() {
  running.store(false);
  if (!stream) return;
  A.requestStop(stream);
  A.close(stream);
  stream = nullptr;
}

const char* SSMic_ErrorText(int r) {
  if (r == -1000) return "AAudio not available";
  return (A.convertResultToText != nullptr) ? A.convertResultToText(r) : "error";
}

// info (10): rate, channels, burst, buffer, performance mode, sharing mode, xruns, failed, callbacks
void SSMic_Info(int* out) {
  out[0] = inRate.load(); out[1] = channels.load(); out[2] = burst.load();
  out[3] = stream ? A.getBufferSizeInFrames(stream) : 0;
  out[4] = stream ? A.getPerformanceMode(stream) : 0;
  out[5] = stream ? A.getSharingMode(stream) : -1;
  out[6] = stream ? A.getXRunCount(stream) : 0;
  out[7] = failed.load() ? 1 : 0;
  out[8] = callbacks.load();
  out[9] = 0;
}

int SSMic_OpenReader(int outRate, float targetMs) {
  for (int i = 0; i < READERS; i++) {
    bool expect = false;
    if (readers[i].used.compare_exchange_strong(expect, true)) {
      Reader& R = readers[i];
      R.primed = false; R.outRate = outRate > 0 ? outRate : 48000; R.targetMs = targetMs;
      R.underruns = 0; R.drops = 0; R.fillMin = 1 << 30; R.fillMax = 0; R.reads = 0; R.trim = 0;
      return i;
    }
  }
  return -1;
}

void SSMic_CloseReader(int id) {
  if (id >= 0 && id < READERS) readers[id].used.store(false);
}

// fill `len` interleaved samples (`ch` channels) with microphone audio at the output rate
// (each channel the same); silence until there's enough. Called on Unity's audio thread.
void SSMic_Read(int id, float* out, int len, int ch) {
  if (ch < 1) ch = 1;
  int frames = len / ch;
  if (id < 0 || id >= READERS || !readers[id].used.load() || !running.load()) { memset(out, 0, len * sizeof(float)); return; }
  Reader& R = readers[id];
  int rate = inRate.load();
  if (rate <= 0) { memset(out, 0, len * sizeof(float)); return; }
  double ratio = (double)rate / R.outRate;
  int64_t w = written.load(std::memory_order_acquire);
  double need = frames * ratio + 2;
  int b = burst.load();
  double target = fmax(rate * R.targetMs / 1000.0, (double)(b > 0 ? b : 0) * 1.5);
  R.reads.fetch_add(1, std::memory_order_relaxed);

  if (!R.primed) {
    if (w < target + need) { memset(out, 0, len * sizeof(float)); return; }
    R.pos = (double)w - target - need;
    R.primed = true;
  }
  double avail = (double)w - R.pos;
  int fill = (int)(avail - need);
  if (fill < R.fillMin.load(std::memory_order_relaxed)) R.fillMin.store(fill, std::memory_order_relaxed);
  if (fill > R.fillMax.load(std::memory_order_relaxed)) R.fillMax.store(fill, std::memory_order_relaxed);

  if (avail < need) { // underrun: silence, fill up again
    memset(out, 0, len * sizeof(float));
    R.underruns.fetch_add(1, std::memory_order_relaxed);
    R.primed = false;
    return;
  }
  if (avail > need + target * 4 + rate * 0.02 || avail > RING / 2) { // far behind (a stall): jump forward
    R.pos = (double)w - target - need;
    R.drops.fetch_add(1, std::memory_order_relaxed);
    avail = (double)w - R.pos;
  }
  // follow the clock drift: read up to 0.2% faster or slower to stay at the target
  double err = (avail - need - target) / rate; // seconds over the target
  double trim = err * 0.2;
  if (trim > 0.002) trim = 0.002; else if (trim < -0.002) trim = -0.002;
  R.trim = (float)trim;
  double step = ratio * (1.0 + trim);

  double p = R.pos;
  for (int i = 0; i < frames; i++) {
    int64_t k = (int64_t)p;
    float f = (float)(p - (double)k);
    float a = ring[k & (RING - 1)], c = ring[(k + 1) & (RING - 1)];
    float v = a + (c - a) * f;
    for (int j = 0; j < ch; j++) out[i * ch + j] = v;
    p += step;
  }
  R.pos = p;
}

// a reader's stats since the last call: reads, underruns, drops, fill min/max (input
// frames, beyond what one read needs), trim (x 1e6); plus callbacks and peak (x 1000)
void SSMic_Stats(int id, int* out) {
  if (id < 0 || id >= READERS) return;
  Reader& R = readers[id];
  out[0] = R.reads.exchange(0); out[1] = R.underruns.exchange(0); out[2] = R.drops.exchange(0);
  int mn = R.fillMin.exchange(1 << 30), mx = R.fillMax.exchange(0);
  out[3] = mn == (1 << 30) ? -1 : mn; out[4] = mx;
  out[5] = (int)(R.trim * 1e6f);
  out[6] = callbacks.load(); out[7] = peakMilli.exchange(0);
}

} // extern "C"

#endif // __ANDROID__
