#include "pipe.h"

#include <array>
#include <cwchar>

namespace uv {
namespace {

constexpr DWORD pipe_buffer = 256 * 1024;
constexpr DWORD connect_timeout = 60000;
constexpr DWORD write_timeout = 120000;
constexpr wchar_t pipe_prefix[] = LR"(\\.\pipe\dynamaker-uv-)";

bool is_hex(wchar_t c) {
    return (c >= L'0' && c <= L'9') || (c >= L'a' && c <= L'f') || (c >= L'A' && c <= L'F');
}

} // namespace

bool LocalPipe::valid_name(const wchar_t *name) {
    constexpr auto prefix_length = std::size(pipe_prefix) - 1;
    if (name == nullptr || std::wcslen(name) != prefix_length + 32 || std::wcsncmp(name, pipe_prefix, prefix_length) != 0) {
        return false;
    }
    for (std::size_t i = prefix_length; i < prefix_length + 32; ++i) {
        if (!is_hex(name[i])) return false;
    }
    return true;
}

DWORD LocalPipe::create(const wchar_t *name) {
    if (!valid_name(name)) return ERROR_INVALID_NAME;
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return GetLastError();
    alignas(TOKEN_USER) std::array<std::uint8_t, sizeof(TOKEN_USER) + SECURITY_MAX_SID_SIZE> info{};
    DWORD returned = 0;
    const auto got_user = GetTokenInformation(token, TokenUser, info.data(), static_cast<DWORD>(info.size()), &returned);
    const auto token_error = got_user ? ERROR_SUCCESS : GetLastError();
    CloseHandle(token);
    if (token_error != ERROR_SUCCESS) return token_error;
    const auto user = reinterpret_cast<const TOKEN_USER *>(info.data())->User.Sid;
    alignas(ACL) std::array<std::uint8_t, sizeof(ACL) + sizeof(ACCESS_ALLOWED_ACE) + SECURITY_MAX_SID_SIZE> acl_buffer{};
    auto *acl = reinterpret_cast<ACL *>(acl_buffer.data());
    SECURITY_DESCRIPTOR descriptor{};
    if (!InitializeAcl(acl, static_cast<DWORD>(acl_buffer.size()), ACL_REVISION) ||
        !AddAccessAllowedAce(acl, ACL_REVISION, FILE_ALL_ACCESS, user) ||
        !InitializeSecurityDescriptor(&descriptor, SECURITY_DESCRIPTOR_REVISION) ||
        !SetSecurityDescriptorDacl(&descriptor, TRUE, acl, FALSE) ||
        !SetSecurityDescriptorControl(&descriptor, SE_DACL_PROTECTED, SE_DACL_PROTECTED)) {
        return GetLastError();
    }
    SECURITY_ATTRIBUTES sa{sizeof(sa), &descriptor, FALSE};
    io_event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!io_event_) return GetLastError();
    canceled_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!canceled_) return GetLastError();
    // The caller cannot substitute an existing instance, and remote pipe clients are rejected.
    handle_ = CreateNamedPipeW(name, PIPE_ACCESS_OUTBOUND | FILE_FLAG_FIRST_PIPE_INSTANCE | FILE_FLAG_OVERLAPPED,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, pipe_buffer, 0, 0, &sa);
    return handle_ == INVALID_HANDLE_VALUE ? GetLastError() : ERROR_SUCCESS;
}

DWORD LocalPipe::wait_io(OVERLAPPED &io, DWORD &transferred, DWORD timeout) {
    const HANDLE handles[]{io_event_, canceled_};
    const auto result = WaitForMultipleObjects(2, handles, FALSE, timeout);
    if (result == WAIT_OBJECT_0) {
        return GetOverlappedResult(handle_, &io, &transferred, FALSE) ? ERROR_SUCCESS : GetLastError();
    }
    const auto error = result == WAIT_TIMEOUT ? ERROR_TIMEOUT : result == WAIT_OBJECT_0 + 1 ? ERROR_OPERATION_ABORTED : GetLastError();
    CancelIoEx(handle_, &io);
    // OVERLAPPED and its buffers cannot leave scope until even a canceled operation has completed.
    GetOverlappedResult(handle_, &io, &transferred, TRUE);
    return error;
}

DWORD LocalPipe::connect() {
    if (WaitForSingleObject(canceled_, 0) == WAIT_OBJECT_0) return ERROR_OPERATION_ABORTED;
    ResetEvent(io_event_);
    OVERLAPPED io{};
    io.hEvent = io_event_;
    if (ConnectNamedPipe(handle_, &io)) return ERROR_SUCCESS;
    const auto error = GetLastError();
    if (error == ERROR_PIPE_CONNECTED) return ERROR_SUCCESS; // The client beat ConnectNamedPipe.
    if (error != ERROR_IO_PENDING) return error;
    DWORD transferred = 0;
    return wait_io(io, transferred, connect_timeout);
}

DWORD LocalPipe::write(const std::uint8_t *data, std::size_t bytes) {
    while (bytes != 0) {
        if (WaitForSingleObject(canceled_, 0) == WAIT_OBJECT_0) return ERROR_OPERATION_ABORTED;
        const auto count = bytes > pipe_buffer ? pipe_buffer : static_cast<DWORD>(bytes);
        ResetEvent(io_event_);
        OVERLAPPED io{};
        io.hEvent = io_event_;
        DWORD written = 0;
        if (!WriteFile(handle_, data, count, &written, &io)) {
            auto error = GetLastError();
            if (error == ERROR_IO_PENDING) error = wait_io(io, written, write_timeout);
            if (error != ERROR_SUCCESS) return error;
        }
        if (written == 0) return ERROR_WRITE_FAULT;
        data += written;
        bytes -= written;
    }
    return ERROR_SUCCESS;
}

DWORD LocalPipe::finish() {
    // DisconnectNamedPipe would discard unread bytes. Flush waits until the client reads them.
    // The supervisor owns the overall deadline and closes the reader first on cancel/failure.
    const auto error = FlushFileBuffers(handle_) ? ERROR_SUCCESS : GetLastError();
    close_writer(); // EOF for the sole reader.
    return error;
}

void LocalPipe::close_writer() {
    if (handle_ != INVALID_HANDLE_VALUE) {
        CloseHandle(handle_);
        handle_ = INVALID_HANDLE_VALUE;
    }
}

void LocalPipe::cancel() {
    if (canceled_) SetEvent(canceled_);
}

bool LocalPipe::wait_for_delay(DWORD millis) const {
    return WaitForSingleObject(canceled_, millis) == WAIT_TIMEOUT;
}

LocalPipe::~LocalPipe() {
    close_writer();
    if (io_event_) CloseHandle(io_event_);
    if (canceled_) CloseHandle(canceled_);
}

} // namespace uv
