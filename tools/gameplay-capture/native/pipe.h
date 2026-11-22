#pragma once

#include <windows.h>
#include <cstddef>
#include <cstdint>

namespace uv {

// Owns the server handle, an overlapped I/O event, and a separate manual-reset stop event.
class LocalPipe {
public:
    LocalPipe() = default;
    ~LocalPipe();
    LocalPipe(const LocalPipe &) = delete;
    LocalPipe &operator=(const LocalPipe &) = delete;

    static bool valid_name(const wchar_t *name);
    DWORD create(const wchar_t *name);
    DWORD connect();
    DWORD write(const std::uint8_t *data, std::size_t bytes);
    DWORD finish(); // Flush accepted bytes to the reader and close without discarding the pipe buffer.
    void close_writer();
    void cancel(); // Signal only; can be called from the producing thread.
    bool wait_for_delay(DWORD millis) const;

private:
    DWORD wait_io(OVERLAPPED &io, DWORD &transferred, DWORD timeout);
    HANDLE handle_ = INVALID_HANDLE_VALUE;
    HANDLE io_event_ = nullptr;
    HANDLE canceled_ = nullptr;
};

} // namespace uv
