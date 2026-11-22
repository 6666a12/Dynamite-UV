#include "matroska.h"

#include <algorithm>
#include <cstring>
#include <initializer_list>
#include <limits>
#include <string_view>

namespace uv {
namespace {

std::size_t integer_size(std::uint64_t value) {
    std::size_t n = 1;
    while ((value >>= 8) != 0) ++n;
    return n;
}

void put_be(std::uint8_t *&out, std::uint64_t value, std::size_t n) {
    while (n != 0) {
        --n;
        *out++ = static_cast<std::uint8_t>(value >> (8 * n));
    }
}

void put_header(std::uint8_t *&out, std::uint32_t id, std::uint64_t length) {
    put_be(out, id, integer_size(id));
    std::size_t n = 1;
    // All-ones payload is the EBML unknown-size sentinel, not a known size.
    while (length >= (std::uint64_t{1} << (7 * n)) - 1) ++n;
    put_be(out, length | (std::uint64_t{1} << (7 * n)), n);
}

Bytes element(std::uint32_t id, const Bytes &payload) {
    std::array<std::uint8_t, 12> header{};
    auto *p = header.data();
    put_header(p, id, payload.size());
    Bytes data(header.data(), p);
    data.insert(data.end(), payload.begin(), payload.end());
    return data;
}

Bytes integer(std::uint32_t id, std::uint64_t value) {
    Bytes data(integer_size(value));
    auto *p = data.data();
    put_be(p, value, data.size());
    return element(id, data);
}

Bytes text(std::uint32_t id, std::string_view value) {
    return element(id, Bytes(value.begin(), value.end()));
}

Bytes real(std::uint32_t id, double value) {
    static_assert(sizeof(double) == 8 && std::numeric_limits<double>::is_iec559);
    std::uint64_t bits = 0;
    std::memcpy(&bits, &value, sizeof(bits));
    Bytes data(8);
    auto *p = data.data();
    put_be(p, bits, data.size());
    return element(id, data);
}

Bytes join(std::initializer_list<Bytes> pieces) {
    std::size_t length = 0;
    for (const auto &piece : pieces) length += piece.size();
    Bytes joined;
    joined.reserve(length);
    for (const auto &piece : pieces) joined.insert(joined.end(), piece.begin(), piece.end());
    return joined;
}

Bytes track(std::uint8_t number, std::string_view codec, Bytes settings) {
    return element(0xAE, join({integer(0xD7, number), integer(0x73C5, number), integer(0x83, number),
        integer(0x9C, 0), text(0x86, codec), settings})); // FlagLacing: off.
}

void block(std::uint8_t *&out, std::uint8_t track_number, std::size_t bytes) {
    put_header(out, 0xA3, bytes + 4); // SimpleBlock and its four-byte header.
    *out++ = static_cast<std::uint8_t>(0x80 | track_number);
    *out++ = 0; // Relative timestamp: both blocks start at the Cluster timestamp.
    *out++ = 0;
    *out++ = 0x80; // Keyframe; raw pixel and PCM blocks are independent.
}

} // namespace

Bytes stream_header() {
    const auto ebml = element(0x1A45DFA3, join({integer(0x4286, 1), integer(0x42F7, 1), integer(0x42F2, 4),
        integer(0x42F3, 8), text(0x4282, "matroska"), integer(0x4287, 4), integer(0x4285, 2)}));
    const Bytes unknown_segment{0x18, 0x53, 0x80, 0x67, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
    const auto info = element(0x1549A966, join({integer(0x2AD7B1, 1000), // TimestampScale: 1 µs (in ns).
        text(0x4D80, "DynaMaker UV"), text(0x5741, "DynaMaker UV")}));
    const auto video = track(1, "V_UNCOMPRESSED", join({integer(0x23E383, (1000000000 + fps / 2) / fps),
        element(0xE0, join({integer(0xB0, width), integer(0xBA, height), text(0x2EB524, "RGBA")}))}));
    const auto audio = track(2, "A_PCM/INT/LIT", element(0xE1, join({real(0xB5, sample_rate), integer(0x9F, 2), integer(0x6264, 32)})));
    return join({ebml, unknown_segment, info, element(0x1654AE6B, join({video, audio}))});
}

Cluster cluster_header(std::uint64_t frame) {
    Cluster cluster;
    std::array<std::uint8_t, 10> time{};
    auto *end = time.data();
    // Calculate directly from frame number, not by adding a rounded frame duration.
    const auto us = (frame / fps) * 1000000 + ((frame % fps) * 1000000 + fps / 2) / fps;
    put_header(end, 0xE7, integer_size(us));
    put_be(end, us, integer_size(us));
    const auto time_size = static_cast<std::size_t>(end - time.data());
    auto *prefix = cluster.prefix.data();
    constexpr std::size_t video_header_size = 9; // A3, size VINT (4 bytes for a 1080p RGBA block), track+time+flags.
    put_header(prefix, 0x1F43B675, time_size + video_header_size + rgba_bytes + cluster.audio_header.size() + pcm_bytes);
    prefix = std::copy_n(time.data(), time_size, prefix);
    block(prefix, 1, rgba_bytes);
    cluster.prefix_size = static_cast<std::size_t>(prefix - cluster.prefix.data());
    auto *audio = cluster.audio_header.data();
    block(audio, 2, pcm_bytes);
    return cluster;
}

} // namespace uv
