#include "stream.h"

#include <atomic>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <future>

using namespace std::chrono_literals;

namespace {
std::atomic<unsigned> errors{0};
void record_error(const char *message, DWORD code) noexcept {
    std::fprintf(stderr, "stream report: %s (%lu)\n", message, static_cast<unsigned long>(code));
    ++errors;
}

void require(bool success, const char *message) {
    if (success) return;
    std::fprintf(stderr, "FAIL: %s\n", message);
    std::exit(1);
}

std::wstring new_pipe(unsigned suffix) {
    wchar_t name[90];
    std::swprintf(name, std::size(name), LR"(\\.\pipe\dynamaker-uv-%08lX%016llX%08X)",
        static_cast<unsigned long>(GetCurrentProcessId()), static_cast<unsigned long long>(GetTickCount64()), suffix);
    return name;
}

HANDLE open_reader(const std::wstring &name) {
    auto handle = CreateFileW(name.c_str(), GENERIC_READ, 0, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    require(handle != INVALID_HANDLE_VALUE, "create same-user local reader");
    return handle;
}

void read_exact(HANDLE file, std::uint8_t *dest, std::size_t length) {
    while (length != 0) {
        DWORD read = 0;
        require(ReadFile(file, dest, static_cast<DWORD>(length), &read, nullptr) && read != 0, "read complete stream bytes");
        dest += read;
        length -= read;
    }
}

void check_bytes(HANDLE pipe, const std::uint8_t *expected, std::size_t size, uv::Bytes &scratch) {
    read_exact(pipe, scratch.data(), size);
    require(std::memcmp(scratch.data(), expected, size) == 0, "exact expected Matroska bytes");
}

void fill(uv::Slot &slot, unsigned frame) {
    std::memset(slot.rgba(), static_cast<int>(frame), uv::rgba_bytes);
    std::memset(slot.pcm(), static_cast<int>(frame + 63), uv::pcm_bytes);
}

void wait_for_commits(const std::atomic<unsigned> &commits, unsigned want) {
    const auto end = std::chrono::steady_clock::now() + 5s;
    while (commits < want && std::chrono::steady_clock::now() < end) std::this_thread::sleep_for(1ms);
    require(commits == want, "fill exactly the expected number of frames");
}

void drain_and_check_backpressure() {
    const auto name = new_pipe(1);
    uv::FrameStream stream(record_error);
    require(stream.start(name.c_str()), "start outbound named pipe");
    uv::LocalPipe duplicate;
    require(duplicate.create(name.c_str()) != ERROR_SUCCESS, "reject a second server for the same pipe");
    const HANDLE reader = open_reader(name);
    std::atomic<unsigned> commits{0};
    auto producer = std::async(std::launch::async, [&] {
        uv::Slot *slot = nullptr;
        for (unsigned i = 0; i < 3; ++i) require(stream.claim(slot) == uv::FrameStream::Claim::discard, "discard warmup");
        require(stream.arm() && !stream.arm(), "arm only once");
        for (unsigned i = 0; i < 24; ++i) {
            require(stream.claim(slot) == uv::FrameStream::Claim::reserved, "reserve next slot");
            fill(*slot, i);
            require(stream.publish(), "publish filled slot");
            ++commits;
        }
        uv::Finished done{};
        require(stream.finish(done), "drain queue and finish successfully");
        return done;
    });
    // The reader is open but not reading. The first active write must still own its slot.
    wait_for_commits(commits, uv::slots);
    require(producer.wait_for(150ms) == std::future_status::timeout && commits == uv::slots, "11th slot blocks while the first write is in flight");
    uv::Bytes scratch(uv::rgba_bytes);
    const auto header = uv::stream_header();
    check_bytes(reader, header.data(), header.size(), scratch);
    for (unsigned i = 0; i < 24; ++i) {
        const auto cluster = uv::cluster_header(i);
        check_bytes(reader, cluster.prefix.data(), cluster.prefix_size, scratch);
        read_exact(reader, scratch.data(), uv::rgba_bytes);
        for (std::size_t n = 0; n < uv::rgba_bytes; ++n) require(scratch[n] == i, "complete RGBA frame, no repetition or drop");
        check_bytes(reader, cluster.audio_header.data(), cluster.audio_header.size(), scratch);
        read_exact(reader, scratch.data(), uv::pcm_bytes);
        for (std::size_t n = 0; n < uv::pcm_bytes; ++n) require(scratch[n] == i + 63, "paired PCM block, no repetition or drop");
    }
    DWORD read = 0;
    require(!ReadFile(reader, scratch.data(), 1, &read, nullptr) && GetLastError() == ERROR_BROKEN_PIPE, "receive clean EOF");
    CloseHandle(reader);
    const auto done = producer.get();
    require(done.frames == 24 && done.audio_samples_per_channel == 19200 && done.first_source_frame == 3 && done.max_slots_used == 10,
        "complete finished counts");
    require(errors == 0, "no unexpected stream errors");
}

void cancel_while_full() {
    const auto name = new_pipe(2);
    uv::FrameStream stream(record_error);
    require(stream.start(name.c_str()), "start cancellation case");
    const HANDLE reader = open_reader(name);
    std::atomic<unsigned> commits{0};
    auto producer = std::async(std::launch::async, [&] {
        require(stream.arm(), "arm cancellation case");
        for (unsigned i = 0; i < 20; ++i) {
            uv::Slot *slot = nullptr;
            if (stream.claim(slot) != uv::FrameStream::Claim::reserved) break;
            fill(*slot, i);
            if (!stream.publish()) break;
            ++commits;
        }
        uv::Finished result{};
        return stream.finish(result);
    });
    wait_for_commits(commits, uv::slots);
    CloseHandle(reader); // Supervisor/encoder failure breaks the in-flight write.
    require(producer.wait_for(5s) == std::future_status::ready && !producer.get(), "closing a stalled reader releases recorder and fails export");
    require(errors == 1, "one fatal report for disconnected consumer");
}

void flush_unread_tail(bool close_reader) {
    const auto name = new_pipe(close_reader ? 5 : 4);
    uv::LocalPipe pipe;
    require(pipe.create(name.c_str()) == ERROR_SUCCESS, "start final pipe drain case");
    const HANDLE reader = open_reader(name);
    require(pipe.connect() == ERROR_SUCCESS, "connect final pipe drain reader");
    const std::uint8_t tail[]{0x91, 0x82, 0x73, 0x64, 0x55};
    require(pipe.write(tail, sizeof(tail)) == ERROR_SUCCESS, "tail accepted by Win32 before flush");
    DWORD available = 0;
    require(PeekNamedPipe(reader, nullptr, 0, nullptr, &available, nullptr) && available == sizeof(tail),
        "reader still has all accepted tail bytes pending");

    std::promise<void> entering_flush;
    auto entered = entering_flush.get_future();
    auto writer = std::async(std::launch::async, [&] {
        entering_flush.set_value();
        return pipe.finish();
    });
    require(entered.wait_for(5s) == std::future_status::ready, "schedule the final flush");
    require(writer.wait_for(150ms) == std::future_status::timeout, "flush blocks while accepted tail is unread");
    if (close_reader) {
        // The supervisor closes the encoder on cancellation/failure, releasing the synchronous flush.
        CloseHandle(reader);
        require(writer.wait_for(5s) == std::future_status::ready && writer.get() != ERROR_SUCCESS,
            "closing reader releases final flush without confirming a complete stream");
    } else {
        uv::Bytes bytes(sizeof(tail));
        check_bytes(reader, tail, sizeof(tail), bytes);
        require(writer.wait_for(5s) == std::future_status::ready && writer.get() == ERROR_SUCCESS,
            "consuming final tail releases flush successfully");
        DWORD read = 0;
        require(!ReadFile(reader, bytes.data(), 1, &read, nullptr) && GetLastError() == ERROR_BROKEN_PIPE,
            "final tail drained before reader received EOF");
        CloseHandle(reader);
    }
}

void stop_awaiting_connection() {
    const auto name = new_pipe(3);
    uv::FrameStream stream(record_error);
    require(stream.start(name.c_str()), "start waiting for a reader");
    uv::Finished result{};
    require(!stream.finish(result), "empty export cancels a wait for an absent reader");
    require(errors == 2, "one fatal report for no captured frames");
}
} // namespace

int main() {
    require(!uv::LocalPipe::valid_name(LR"(\\example\pipe\dynamaker-uv-00000000000000000000000000000000)"), "reject remote pipe path");
    require(!uv::LocalPipe::valid_name(LR"(\\.\pipe\dynamaker-uv-0000000000000000000000000000000g)"), "reject non-hex session name");
    drain_and_check_backpressure();
    cancel_while_full();
    flush_unread_tail(false);
    flush_unread_tail(true);
    stop_awaiting_connection();
    std::puts("UV STREAM TEST PASS");
}
