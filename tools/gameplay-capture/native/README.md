# UV Video Stream (Windows x64)

Capture-worker-only Godot `MovieWriter` GDExtension. It uses the official engine's fixed 60 FPS viewport readbacks and Dummy driver's offline audio mix. It sends one forward-only Matroska stream (1080p RGBA8 + 48 kHz stereo s32le) to the supervisor's external FFmpeg reader, and writes no media files. Nothing links or redistributes FFmpeg.

## Build

Use the official Godot **4.7.1 stable Mono for Windows**, commit `a13da4feb`, to create the external SDK dump in a scratch directory:

```powershell
# Run both commands in an empty directory; the engine writes the dumps there.
& <godot-mono-console.exe> --headless --dump-extension-api
& <godot-mono-console.exe> --headless --dump-gdextension-interface-json
```

Check out [godot-cpp at `7fd5c4bc3efda6487fd6e6486c50016faa622f92`](https://github.com/godotengine/godot-cpp/commit/7fd5c4bc3efda6487fd6e6486c50016faa622f92) outside the source tree. The [CMake configuration](<CMakeLists.txt>) enforces the revision, a clean tracked checkout, and the exact Windows API and interface JSON hashes. Nothing is downloaded or vendored by CMake.

```powershell
cmake -S tools/gameplay-capture/native -B .tmp/uv-native-build -G Ninja `
    -DCMAKE_BUILD_TYPE=Release -DCMAKE_CXX_COMPILER=g++ `
    "-DUV_GODOT_CPP=C:/absolute/godot-cpp" "-DUV_GODOT_API=C:/absolute/api-dump"
cmake --build .tmp/uv-native-build
ctest --test-dir .tmp/uv-native-build --output-on-failure
```

Ninja emits `uv_video_stream.dll`, `uv_video_stream.gdextension` and `uv_video_stream.variant` in the build directory. Pass that directory with `--capture-native` to the [Windows candidate builder](<../../editor-release/README.md#optional-chart-video-export>). It stages the extension at `res://native/video_stream/` only for the capture export, verifies the exported extension list and attaches the [godot-cpp MIT notice](<GODOT-CPP-LICENSE.md>). The descriptor disables reload; the engine selects the writer by `.uvstream`, but the selector path is never created.

## Supervisor protocol, version 1

- Pass `--write-movie <private scratch>/capture.uvstream --fixed-fps 60 --audio-driver Dummy`, and set `DU_CAPTURE_PIPE` to a newly randomized `\\.\pipe\dynamaker-uv-<32 hex>` path. The writer rejects all other paths, prevents a second instance for that name, uses a DACL for the process's account, and rejects remote clients. The handle is not inheritable.
- Move the external process to the consumption stage after the single `UV_VIDEO_STREAM {"event":"pipe-ready","version":1}` stdout line. Pass `-f matroska -seekable 0 -i "\\.\pipe\dynamaker-uv-…"` directly to the FFmpeg executable, with `-fps_mode passthrough` to preserve the source frames. Accept and write run on a dedicated thread; `pipe-ready` means the server exists, not that FFmpeg has connected.
- The Godot singleton `UVVideoStream` exposes `healthy()` and one-shot `arm()`. `arm()` affects the **next** native movie callback. All previous callbacks are discarded; `firstSourceFrame` is that next callback's **zero-based index including discarded warmup**. Images and their 800 stereo audio samples per channel share one slot and start at timestamp zero in the delivered stream. Subsequent timestamps are derived from the frame index at 60 FPS, rounded independently to microseconds; there is no elapsed-clock accumulator or compensation based on pipe speed.
- There are exactly 10 reusable slots (~79.2 MiB of pixel/PCM payload, excluding Godot and FFmpeg). Reserving/copying and in-flight pipe writes are counted. After ten live slots, `_write_frame` waits without advancing another game tick. It never overwrites, duplicates or drops an armed frame. Only the capture thread touches Godot API; it copies borrowed audio before returning.
- One `finished` record is emitted from `_write_end` only after queue drain, flushed pipe bytes, and close/EOF, with `frames`, `audioSamplesPerChannel`, `firstSourceFrame` and `maxSlotsUsed`. It is not proof that FFmpeg finished or that the managed capture passed. `error` records have `message` and optional meaning from numeric `win32` (0 when not a Windows I/O error); the first error wakes waiters. The engine may ignore native `Error` returns, so supervise these status records in addition to process exit and the managed report.
- Accept and overlapped writes have deadlines (60 s to connect, 120 s per blocked 256 KiB write). The supervisor also checks for three minutes without scene or encoder progress and cancels **both** processes on inactivity, failure or user cancellation, closing/terminating the reader before or alongside the writer; this also releases the final Win32 pipe flush if blocked. Do not publish the temporary MP4 on any failure or cancellation.

## Backpressure validation

`uv_stream_test` requires no graphical Godot. It opens a real local named pipe, holds its reader until the queue fills and the 11th frame blocks, then verifies 24 exact paired frame payloads and a clean EOF. It verifies disconnect while the queue is full, a writer stopped before connection, and pipe-name/instance validation.

An opt-in QA build (`-DUV_STREAM_ENABLE_QA=ON`) honors `DU_CAPTURE_QA_DELAY_MS` (integer 0–1000 ms per consumed frame) and a one-time `DU_CAPTURE_QA_PAUSE_AT` (zero-based stream frame, 0–108000) plus `DU_CAPTURE_QA_PAUSE_MS` (0–10000 ms). The variables are ignored in the default production build. This instruments the consumer, not the game clock; run identical fixtures without the delays and with a deliberate stall to compare exact frame/sample counts and content. This is backpressure testing, not hardware equivalence or physical HDD testing.
