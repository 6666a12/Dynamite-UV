#include "stream.h"

#include <godot_cpp/classes/audio_server.hpp>
#include <godot_cpp/classes/engine.hpp>
#include <godot_cpp/classes/image.hpp>
#include <godot_cpp/classes/movie_writer.hpp>
#include <godot_cpp/core/class_db.hpp>
#include <godot_cpp/godot.hpp>

#include <cinttypes>
#include <cstdio>
#include <cstring>

namespace {
using namespace godot;

void stdout_line(const char *line) noexcept {
    // Do not attach a console or use both Godot's logger and the CRT stdout for the same event.
    // GUI workers launched with redirected stdout inherit this handle from the Shell.
    const auto out = GetStdHandle(STD_OUTPUT_HANDLE);
    if (out && out != INVALID_HANDLE_VALUE) {
        DWORD ignored = 0;
        WriteFile(out, line, static_cast<DWORD>(std::strlen(line)), &ignored, nullptr);
    }
}

void native_error(const char *message, DWORD win32) noexcept {
    // Only internal JSON-safe literals are passed; untrusted path/environment text is never interpolated.
    char line[512];
    std::snprintf(line, sizeof(line), "UV_VIDEO_STREAM {\"event\":\"error\",\"version\":1,\"message\":\"%s\",\"win32\":%lu}\n",
        message, static_cast<unsigned long>(win32));
    stdout_line(line);
}

#ifdef UV_STREAM_ENABLE_QA
bool read_delay(const wchar_t *key, std::uint64_t max, std::uint64_t &value) {
    wchar_t digits[24]{};
    const auto count = GetEnvironmentVariableW(key, digits, static_cast<DWORD>(std::size(digits)));
    value = 0;
    if (count >= std::size(digits)) return false;
    for (DWORD i = 0; i < count; ++i) {
        if (digits[i] < L'0' || digits[i] > L'9') return false;
        value = value * 10 + static_cast<unsigned>(digits[i] - L'0');
        if (value > max) return false;
    }
    return true;
}
#endif

class UvVideoStream final : public MovieWriter {
    GDCLASS(UvVideoStream, MovieWriter)

protected:
    static void _bind_methods() {
        ClassDB::bind_method(D_METHOD("arm"), &UvVideoStream::arm);
        ClassDB::bind_method(D_METHOD("healthy"), &UvVideoStream::healthy);
    }

public:
    bool arm() { return stream_.arm(); }
    bool healthy() const { return stream_.healthy(); }

    bool _handles_file(const String &path) const override { return path.get_extension() == "uvstream"; }
    PackedStringArray _get_supported_extensions() const override {
        PackedStringArray suffixes;
        suffixes.push_back("uvstream");
        return suffixes;
    }
    std::uint32_t _get_audio_mix_rate() const override { return uv::sample_rate; }
    AudioServer::SpeakerMode _get_audio_speaker_mode() const override { return AudioServer::SPEAKER_MODE_STEREO; }

    Error _write_begin(const Vector2i &size, std::uint32_t fps, const String &) override {
        if (size != Vector2i(uv::width, uv::height) || fps != uv::fps) {
            stream_.fail("Stream export requires 1920x1080 at 60 fps");
            return ERR_INVALID_DATA;
        }
        wchar_t pipe_name[96]{};
        const auto count = GetEnvironmentVariableW(L"DU_CAPTURE_PIPE", pipe_name, static_cast<DWORD>(std::size(pipe_name)));
        if (count == 0 || count >= std::size(pipe_name) || !uv::LocalPipe::valid_name(pipe_name)) {
            stream_.fail("DU_CAPTURE_PIPE is missing or is not a private DynaMaker UV pipe name");
            return ERR_INVALID_PARAMETER;
        }
        uv::QaDelay delay;
#ifdef UV_STREAM_ENABLE_QA
        std::uint64_t per_frame = 0, pause_at = 0, pause_ms = 0;
        if (!read_delay(L"DU_CAPTURE_QA_DELAY_MS", 1000, per_frame) ||
            !read_delay(L"DU_CAPTURE_QA_PAUSE_AT", 108000, pause_at) ||
            !read_delay(L"DU_CAPTURE_QA_PAUSE_MS", 10000, pause_ms)) {
            stream_.fail("Invalid stream QA delay settings");
            return ERR_INVALID_PARAMETER;
        }
        delay = {static_cast<unsigned>(per_frame), pause_at, static_cast<unsigned>(pause_ms)};
#endif
        try {
            if (!stream_.start(pipe_name, delay)) return ERR_CANT_CREATE;
        } catch (...) {
            stream_.fail("Unexpected failure starting the video stream");
            return FAILED;
        }
        stdout_line("UV_VIDEO_STREAM {\"event\":\"pipe-ready\",\"version\":1}\n");
        return OK;
    }

    Error _write_frame(const Ref<Image> &image, const void *audio) override {
        try {
            uv::Slot *slot = nullptr;
            switch (stream_.claim(slot)) {
                case uv::FrameStream::Claim::discard: return OK;
                case uv::FrameStream::Claim::failed: return ERR_FILE_CANT_WRITE;
                case uv::FrameStream::Claim::reserved: break;
            }
            if (image.is_null() || audio == nullptr || image->get_size() != Vector2i(uv::width, uv::height)) {
                stream_.fail("Godot returned a missing or incorrectly sized video frame");
                return ERR_INVALID_DATA;
            }
            Ref<Image> frame = image;
            // Godot owns the callback Image; by this point rendering and audio mixing for this tick are done.
            if (frame->get_format() != Image::FORMAT_RGBA8) frame->convert(Image::FORMAT_RGBA8);
            const auto pixels = frame->get_data();
            if (pixels.size() != static_cast<std::int64_t>(uv::rgba_bytes)) {
                stream_.fail("Godot did not return a tightly packed RGBA8 video frame");
                return ERR_INVALID_DATA;
            }
            std::memcpy(slot->rgba(), pixels.ptr(), uv::rgba_bytes);
            // The pinned binding unwraps the ABI pointer slot for us. The bytes are i32 LE,
            // 800 stereo sample pairs at 48 kHz; borrow the memory only during this callback.
            std::memcpy(slot->pcm(), audio, uv::pcm_bytes);
            if (stream_.publish()) return OK;
            stream_.fail("Could not queue a complete movie frame");
            return ERR_FILE_CANT_WRITE;
        } catch (...) {
            stream_.fail("Unexpected failure capturing a Godot frame");
            return FAILED;
        }
    }

    void _write_end() override {
        try {
            uv::Finished result{};
            if (!stream_.finish(result)) return;
            char line[384];
            std::snprintf(line, sizeof(line), "UV_VIDEO_STREAM {\"event\":\"finished\",\"version\":1,\"frames\":%" PRIu64
                ",\"audioSamplesPerChannel\":%" PRIu64 ",\"maxSlotsUsed\":%zu,\"firstSourceFrame\":%" PRIu64 "}\n",
                result.frames, result.audio_samples_per_channel, result.max_slots_used, result.first_source_frame);
            stdout_line(line);
        } catch (...) {
            stream_.fail("Unexpected failure closing the video stream");
        }
    }

private:
    uv::FrameStream stream_{native_error};
};

UvVideoStream *writer = nullptr;

void initialize(ModuleInitializationLevel level) {
    if (level != MODULE_INITIALIZATION_LEVEL_SCENE) return;
    GDREGISTER_CLASS(UvVideoStream);
    writer = memnew(UvVideoStream);
    Engine::get_singleton()->register_singleton("UVVideoStream", writer);
    MovieWriter::add_writer(writer);
}

void deinitialize(ModuleInitializationLevel level) {
    if (level != MODULE_INITIALIZATION_LEVEL_SCENE || writer == nullptr) return;
    Engine::get_singleton()->unregister_singleton("UVVideoStream");
    // No MovieWriter unregister exists; the engine already finished invoking it before SCENE deinit.
    memdelete(writer);
    writer = nullptr;
}

} // namespace

extern "C" GDExtensionBool GDE_EXPORT uv_video_stream_init(GDExtensionInterfaceGetProcAddress get_proc,
    GDExtensionClassLibraryPtr library, GDExtensionInitialization *initialization) {
    godot::GDExtensionBinding::InitObject init(get_proc, library, initialization);
    init.register_initializer(initialize);
    init.register_terminator(deinitialize);
    init.set_minimum_library_initialization_level(godot::MODULE_INITIALIZATION_LEVEL_SCENE);
    return init.init();
}
