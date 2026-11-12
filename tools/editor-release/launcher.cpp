// Native Windows bootstrap: no managed runtime required to reach the fallback.
#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#include <windows.h>
#include <shellapi.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
namespace fs = std::filesystem;

static std::wstring env(const wchar_t* name) {
    DWORD n = GetEnvironmentVariableW(name, nullptr, 0);
    if (!n) return {};
    std::wstring s(n, L'\0');
    DWORD written = GetEnvironmentVariableW(name, s.data(), n);
    if (!written || written >= n) return {};
    s.resize(written); return s;
}
static std::wstring quote(const std::wstring& arg) {
    std::wstring result = L"\""; size_t slashes = 0;
    for (wchar_t c : arg) {
        if (c == L'\\') { ++slashes; continue; }
        if (c == L'\"') { result.append(slashes * 2 + 1, L'\\'); result += c; }
        else { result.append(slashes, L'\\'); result += c; }
        slashes = 0;
    }
    result.append(slashes * 2, L'\\'); return result + L"\"";
}
static bool x64Executable(const fs::path& path) {
    std::ifstream f(path, std::ios::binary); IMAGE_DOS_HEADER dos{};
    if (!f.read(reinterpret_cast<char*>(&dos), sizeof(dos)) || dos.e_magic != IMAGE_DOS_SIGNATURE || dos.e_lfanew < 0) return false;
    f.seekg(dos.e_lfanew); DWORD signature{}; IMAGE_FILE_HEADER header{};
    return f.read(reinterpret_cast<char*>(&signature), sizeof(signature)) && signature == IMAGE_NT_SIGNATURE &&
        f.read(reinterpret_cast<char*>(&header), sizeof(header)) && header.Machine == IMAGE_FILE_MACHINE_AMD64;
}
static std::wstring registeredRoot() {
    wchar_t value[32768]; DWORD bytes = sizeof(value);
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",
        L"InstallLocation", RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY, nullptr, value, &bytes) == ERROR_SUCCESS) return value;
    return {};
}
static DWORD launch(const fs::path& exe, const std::vector<std::wstring>& args, const fs::path& cwd,
                    bool probe, bool wait) {
    std::wstring command = quote(exe.wstring());
    for (const auto& arg : args) command += L" " + quote(arg);
    STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
    if (!CreateProcessW(exe.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                        nullptr, cwd.c_str(), &startup, &process)) return GetLastError();
    CloseHandle(process.hThread);
    if (!wait) { CloseHandle(process.hProcess); return 0; }
    DWORD status = WaitForSingleObject(process.hProcess, probe ? 15000 : INFINITE), code = 1;
    if (status == WAIT_TIMEOUT) { TerminateProcess(process.hProcess, 124); WaitForSingleObject(process.hProcess, 5000); code = 124; }
    else if (status == WAIT_OBJECT_0) GetExitCodeProcess(process.hProcess, &code);
    CloseHandle(process.hProcess); return code;
}
int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    try {
        wchar_t module[32768]; DWORD n = GetModuleFileNameW(nullptr, module, 32768);
        if (!n || n == 32768) return 1;
        fs::path root = fs::path(module).parent_path(), shell = root / L"shell" / L"DynaMakerUv.Editor.Shell.dll";
        bool bundledOnly = false, checkOnly = false, wait = false;
        int count = 0; LPWSTR* values = CommandLineToArgvW(GetCommandLineW(), &count);
        if (!values) return 1;
        std::vector<std::wstring> forwarded;
        for (int i = 1; i < count; ++i) {
            std::wstring arg = values[i];
            if (arg == L"--bundled-runtime") bundledOnly = true;
            else if (arg == L"--launcher-check") checkOnly = true;
            else if (arg == L"--launcher-wait") wait = true;
            else forwarded.push_back(arg);
        }
        LocalFree(values);
        auto local = env(L"LOCALAPPDATA");
        if (local.empty()) throw std::runtime_error("LOCALAPPDATA is unavailable");
        fs::path logs = fs::path(local) / L"DynaMakerUV" / L"logs"; fs::create_directories(logs);
        std::ofstream log(logs / L"launcher.log", std::ios::app);
        log << "\nLauncher start\n";
        if (!fs::is_regular_file(root / L"distribution.json") || !fs::is_regular_file(shell))
            throw std::runtime_error("Incomplete package");
        // Each probe is a disposable process: incompatible hostfxr cannot contaminate this launcher.
        std::vector<fs::path> candidates;
        if (!bundledOnly) {
            for (auto value : {env(L"DOTNET_ROOT_X64"), env(L"DOTNET_ROOT"), registeredRoot()})
                if (!value.empty()) candidates.emplace_back(value);
            auto programFiles = env(L"ProgramW6432");
            if (programFiles.empty()) programFiles = env(L"ProgramFiles");
            if (!programFiles.empty()) candidates.push_back(fs::path(programFiles) / L"dotnet");
        }
        const auto bundled = root / L"runtime";
        const auto slimShell = root / L"shell-bundled" / L"DynaMakerUv.Editor.Shell.exe";
        const bool slimFallback = fs::is_regular_file(slimShell);
        if (!slimFallback) candidates.push_back(bundled);
        fs::path chosen;
        bool useSlim = false;
        SetEnvironmentVariableW(L"DOTNET_ROLL_FORWARD", L"LatestPatch");
        SetEnvironmentVariableW(L"DOTNET_ROLL_FORWARD_TO_PRERELEASE", L"0");
        SetEnvironmentVariableW(L"DOTNET_MULTILEVEL_LOOKUP", L"0");
        SetEnvironmentVariableW(L"DOTNET_ADDITIONAL_DEPS", nullptr);
        SetEnvironmentVariableW(L"DOTNET_SHARED_STORE", nullptr);
        SetEnvironmentVariableW(L"DOTNET_STARTUP_HOOKS", nullptr);
        for (const auto& candidate : candidates) {
            auto exe = candidate / L"dotnet.exe";
            if (!x64Executable(exe)) continue;
            DWORD code = launch(exe, {shell.wstring(), L"--runtime-check"}, root, true, true);
            log << "Probe " << candidate.u8string() << " exit=" << code << "\n";
            if (!code) { chosen = candidate; break; }
        }
        if (chosen.empty() && slimFallback && x64Executable(slimShell)) {
            DWORD code = launch(slimShell, {L"--runtime-check"}, root, true, true);
            log << "Probe bundled application runtime exit=" << code << "\n";
            if (!code) { chosen = slimShell.parent_path(); useSlim = true; }
        }
        if (chosen.empty()) throw std::runtime_error("Neither system nor bundled .NET 10 x64 could start the editor");
        const bool isBundled = useSlim || chosen == bundled;
        log << "Selected " << (isBundled ? "Bundled" : "System") << " " << chosen.u8string() << "\n"; log.flush();
        SetEnvironmentVariableW(L"DYNAMAKER_RUNTIME_SOURCE", isBundled ? L"Bundled" : L"System");
        SetEnvironmentVariableW(L"DOTNET_ROOT", chosen.c_str());
        SetEnvironmentVariableW(L"DOTNET_ROOT_X64", chosen.c_str());
        if (checkOnly) return 0;
        if (useSlim) return static_cast<int>(launch(slimShell, forwarded, root, false, wait));
        std::vector<std::wstring> args{shell.wstring()}; args.insert(args.end(), forwarded.begin(), forwarded.end());
        // Do not retry a real application failure on a second runtime.
        return static_cast<int>(launch(chosen / L"dotnet.exe", args, root, false, wait));
    } catch (const std::exception&) {
        MessageBoxW(nullptr, L"DynaMaker UV could not start. Extract the complete package again.\nDetails: %LOCALAPPDATA%\\DynaMakerUV\\logs\\launcher.log", L"DynaMaker UV", MB_OK | MB_ICONERROR);
        return 1;
    }
}
