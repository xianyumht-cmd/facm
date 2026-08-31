#include <windows.h>
#include <bcrypt.h>
#include <shellapi.h>

#include <algorithm>
#include <chrono>
#include <cctype>
#include <cstdint>
#include <cwctype>
#include <cwchar>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <optional>
#include <sstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

namespace {

constexpr wchar_t kCoreComponent[] = L"facm-core-win-x64";
constexpr wchar_t kCoreEntryPoint[] = L"FACM.App.exe";
constexpr wchar_t kStateFileName[] = L"active.json";
constexpr wchar_t kCorrelationEnvironment[] = L"FACM_BOOTSTRAP_CORRELATION_ID";
bool g_suppressUi = false;

struct ActiveState {
    int schemaVersion = 1;
    std::wstring activeVersion;
    std::wstring activePath;
    std::wstring previousVersion;
    std::wstring lastSuccessfulLaunch;
};

struct ComponentManifest {
    int schemaVersion = 1;
    std::wstring componentId;
    std::wstring version;
    std::wstring architecture;
    bool required = true;
    std::uintmax_t packageSize = 0;
    std::uintmax_t installedSize = 0;
    std::wstring sha256;
    std::wstring entryPoint;
};

class StatusWindow {
public:
    void Show(const std::wstring& text) {
        if (_window) {
            SetStatus(text);
            return;
        }

        WNDCLASSW klass{};
        klass.lpfnWndProc = &StatusWindow::WindowProc;
        klass.hInstance = GetModuleHandleW(nullptr);
        klass.lpszClassName = L"FACM.Bootstrapper.Status";
        klass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        RegisterClassW(&klass);

        _window = CreateWindowExW(
            WS_EX_TOOLWINDOW,
            klass.lpszClassName,
            L"FACM 初始化",
            WS_CAPTION | WS_SYSMENU,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            440,
            150,
            nullptr,
            nullptr,
            klass.hInstance,
            this);
        if (!_window) return;
        _label = CreateWindowW(
            L"STATIC",
            text.c_str(),
            WS_CHILD | WS_VISIBLE | SS_LEFT,
            24,
            26,
            390,
            70,
            _window,
            nullptr,
            klass.hInstance,
            nullptr);
        ShowWindow(_window, SW_SHOWNORMAL);
        UpdateWindow(_window);
        Pump();
    }

    void SetStatus(const std::wstring& text) {
        if (_label) SetWindowTextW(_label, text.c_str());
        Pump();
    }

    void Close() {
        if (_window) DestroyWindow(_window);
        _window = nullptr;
        _label = nullptr;
    }

private:
    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        if (message == WM_NCCREATE) {
            auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(create->lpCreateParams));
        }
        if (message == WM_CLOSE) {
            DestroyWindow(window);
            return 0;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    static void Pump() {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }

    HWND _window = nullptr;
    HWND _label = nullptr;
};

std::wstring GetModulePath() {
    std::vector<wchar_t> buffer(MAX_PATH);
    for (;;) {
        const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0) return {};
        if (length + 1 < buffer.size()) return std::wstring(buffer.data(), length);
        buffer.resize(buffer.size() * 2);
    }
}

std::wstring NormalizePath(const fs::path& path) {
    try {
        return fs::absolute(path).lexically_normal().wstring();
    } catch (...) {
        return {};
    }
}

bool IsPathInside(const fs::path& root, const fs::path& candidate) {
    const auto rootText = NormalizePath(root);
    const auto candidateText = NormalizePath(candidate);
    if (rootText.empty() || candidateText.empty()) return false;
    auto normalizeForCompare = [](std::wstring value) {
        std::transform(value.begin(), value.end(), value.begin(), towlower);
        while (!value.empty() && (value.back() == L'\\' || value.back() == L'/')) value.pop_back();
        return value;
    };
    const auto rootCompare = normalizeForCompare(rootText);
    const auto candidateCompare = normalizeForCompare(candidateText);
    return candidateCompare == rootCompare ||
           (candidateCompare.size() > rootCompare.size() &&
            candidateCompare.compare(0, rootCompare.size(), rootCompare) == 0 &&
            (candidateCompare[rootCompare.size()] == L'\\' || candidateCompare[rootCompare.size()] == L'/'));
}

std::string WideToUtf8(const std::wstring& value) {
    if (value.empty()) return {};
    const int size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (size <= 0) return {};
    std::string result(static_cast<size_t>(size), '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), size, nullptr, nullptr);
    return result;
}

std::wstring Utf8ToWide(const std::string& value) {
    if (value.empty()) return {};
    const int size = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (size <= 0) return {};
    std::wstring result(static_cast<size_t>(size), L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), result.data(), size);
    return result;
}

std::string EscapeJson(const std::wstring& value) {
    const auto utf8 = WideToUtf8(value);
    std::string result;
    result.reserve(utf8.size() + 8);
    for (const auto character : utf8) {
        switch (character) {
        case '\\': result += "\\\\"; break;
        case '"': result += "\\\""; break;
        case '\r': result += "\\r"; break;
        case '\n': result += "\\n"; break;
        case '\t': result += "\\t"; break;
        default: result.push_back(character); break;
        }
    }
    return result;
}

std::optional<std::string> JsonString(const std::string& json, const std::string& key) {
    const auto marker = "\"" + key + "\"";
    const auto keyPosition = json.find(marker);
    if (keyPosition == std::string::npos) return std::nullopt;
    const auto colon = json.find(':', keyPosition + marker.size());
    if (colon == std::string::npos) return std::nullopt;
    const auto firstQuote = json.find('"', colon + 1);
    if (firstQuote == std::string::npos) return std::nullopt;
    std::string value;
    bool escaped = false;
    for (size_t index = firstQuote + 1; index < json.size(); ++index) {
        const auto character = json[index];
        if (escaped) {
            switch (character) {
            case 'n': value.push_back('\n'); break;
            case 'r': value.push_back('\r'); break;
            case 't': value.push_back('\t'); break;
            default: value.push_back(character); break;
            }
            escaped = false;
            continue;
        }
        if (character == '\\') {
            escaped = true;
            continue;
        }
        if (character == '"') return value;
        value.push_back(character);
    }
    return std::nullopt;
}

std::optional<std::uintmax_t> JsonUnsigned(const std::string& json, const std::string& key) {
    const auto marker = "\"" + key + "\"";
    const auto keyPosition = json.find(marker);
    if (keyPosition == std::string::npos) return std::nullopt;
    const auto colon = json.find(':', keyPosition + marker.size());
    if (colon == std::string::npos) return std::nullopt;
    size_t start = colon + 1;
    while (start < json.size() && isspace(static_cast<unsigned char>(json[start]))) ++start;
    size_t end = start;
    while (end < json.size() && isdigit(static_cast<unsigned char>(json[end]))) ++end;
    if (end == start) return std::nullopt;
    try { return std::stoull(json.substr(start, end - start)); }
    catch (...) { return std::nullopt; }
}

std::optional<bool> JsonBool(const std::string& json, const std::string& key) {
    const auto marker = "\"" + key + "\"";
    const auto keyPosition = json.find(marker);
    if (keyPosition == std::string::npos) return std::nullopt;
    const auto colon = json.find(':', keyPosition + marker.size());
    if (colon == std::string::npos) return std::nullopt;
    const auto valuePosition = json.find_first_not_of(" \t\r\n", colon + 1);
    if (valuePosition == std::string::npos) return std::nullopt;
    if (json.compare(valuePosition, 4, "true") == 0) return true;
    if (json.compare(valuePosition, 5, "false") == 0) return false;
    return std::nullopt;
}

bool ReadText(const fs::path& path, std::string& output) {
    std::ifstream input(path, std::ios::binary);
    if (!input) return false;
    output.assign(std::istreambuf_iterator<char>(input), std::istreambuf_iterator<char>());
    return true;
}

bool AtomicWrite(const fs::path& path, const std::string& content) {
    try {
        fs::create_directories(path.parent_path());
        const auto temporary = path.wstring() + L".tmp-" + std::to_wstring(GetCurrentProcessId());
        {
            std::ofstream output(fs::path(temporary), std::ios::binary | std::ios::trunc);
            if (!output) return false;
            output.write(content.data(), static_cast<std::streamsize>(content.size()));
            output.flush();
            if (!output) return false;
        }
        return MoveFileExW(temporary.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
    } catch (...) {
        return false;
    }
}

std::optional<ActiveState> ReadActiveState(const fs::path& path) {
    std::string json;
    if (!ReadText(path, json)) return std::nullopt;
    const auto activeVersion = JsonString(json, "activeVersion");
    const auto activePath = JsonString(json, "activePath");
    if (!activeVersion || !activePath || activeVersion->empty() || activePath->empty()) return std::nullopt;
    ActiveState state;
    state.activeVersion = Utf8ToWide(*activeVersion);
    state.activePath = Utf8ToWide(*activePath);
    state.previousVersion = Utf8ToWide(JsonString(json, "previousVersion").value_or(""));
    state.lastSuccessfulLaunch = Utf8ToWide(JsonString(json, "lastSuccessfulLaunch").value_or(""));
    return state;
}

std::string ActiveStateJson(const ActiveState& state) {
    std::ostringstream output;
    output << "{\n"
           << "  \"schemaVersion\": 1,\n"
           << "  \"activeVersion\": \"" << EscapeJson(state.activeVersion) << "\",\n"
           << "  \"activePath\": \"" << EscapeJson(state.activePath) << "\",\n"
           << "  \"previousVersion\": \"" << EscapeJson(state.previousVersion) << "\",\n"
           << "  \"lastSuccessfulLaunch\": \"" << EscapeJson(state.lastSuccessfulLaunch) << "\"\n"
           << "}\n";
    return output.str();
}

std::optional<ComponentManifest> ReadComponentManifest(const fs::path& path) {
    std::string json;
    if (!ReadText(path, json)) return std::nullopt;
    const auto componentId = JsonString(json, "componentId");
    const auto version = JsonString(json, "version");
    const auto architecture = JsonString(json, "architecture");
    const auto packageSize = JsonUnsigned(json, "packageSize");
    const auto installedSize = JsonUnsigned(json, "installedSize");
    const auto sha256 = JsonString(json, "sha256");
    const auto entryPoint = JsonString(json, "entryPoint");
    if (!componentId || !version || !architecture || !packageSize || !installedSize || !sha256 || !entryPoint)
        return std::nullopt;
    ComponentManifest manifest;
    manifest.componentId = Utf8ToWide(*componentId);
    manifest.version = Utf8ToWide(*version);
    manifest.architecture = Utf8ToWide(*architecture);
    manifest.packageSize = *packageSize;
    manifest.installedSize = *installedSize;
    manifest.sha256 = Utf8ToWide(*sha256);
    manifest.entryPoint = Utf8ToWide(*entryPoint);
    manifest.required = JsonBool(json, "required").value_or(true);
    return manifest;
}

bool IsHexSha256(const std::wstring& value) {
    if (value.size() != 64) return false;
    return std::all_of(value.begin(), value.end(), [](wchar_t character) {
        return (character >= L'0' && character <= L'9') ||
               (character >= L'a' && character <= L'f') ||
               (character >= L'A' && character <= L'F');
    });
}

std::optional<std::wstring> Sha256File(const fs::path& path) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    std::vector<UCHAR> object;
    std::vector<UCHAR> digest(32);
    DWORD objectLength = 0;
    DWORD resultLength = 0;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0 ||
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &resultLength, 0) < 0 ||
        BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) < 0) {
        if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
        return std::nullopt;
    }

    object.resize(objectLength);
    BCryptDestroyHash(hash);
    hash = nullptr;
    if (BCryptCreateHash(algorithm, &hash, object.data(), objectLength, nullptr, 0, 0) < 0) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return std::nullopt;
    }

    std::ifstream input(path, std::ios::binary);
    if (!input) {
        BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return std::nullopt;
    }
    std::vector<char> buffer(128 * 1024);
    while (input) {
        input.read(buffer.data(), static_cast<std::streamsize>(buffer.size()));
        const auto count = input.gcount();
        if (count > 0 && BCryptHashData(hash, reinterpret_cast<PUCHAR>(buffer.data()), static_cast<ULONG>(count), 0) < 0) {
            BCryptDestroyHash(hash);
            BCryptCloseAlgorithmProvider(algorithm, 0);
            return std::nullopt;
        }
    }
    const auto finished = BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) >= 0;
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    if (!finished) return std::nullopt;

    std::wostringstream output;
    output << std::hex << std::setfill(L'0');
    for (const auto byte : digest) output << std::setw(2) << static_cast<unsigned int>(byte);
    return output.str();
}

std::optional<std::wstring> Sha256Text(const std::string& value) {
    const auto temporary = fs::temp_directory_path() / (L"facm-bootstrap-hash-" + std::to_wstring(GetCurrentProcessId()) + L".tmp");
    {
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        if (!output) return std::nullopt;
        output.write(value.data(), static_cast<std::streamsize>(value.size()));
    }
    const auto result = Sha256File(temporary);
    std::error_code error;
    fs::remove(temporary, error);
    return result;
}

std::uintmax_t DirectorySize(const fs::path& root) {
    std::uintmax_t total = 0;
    std::error_code error;
    for (const auto& entry : fs::recursive_directory_iterator(root, error)) {
        if (error) break;
        if (entry.is_regular_file(error)) total += entry.file_size(error);
    }
    return total;
}

std::wstring DirectoryDigest(const fs::path& root) {
    std::vector<std::wstring> files;
    std::error_code error;
    for (const auto& entry : fs::recursive_directory_iterator(root, error)) {
        if (error || !entry.is_regular_file(error)) continue;
        files.push_back(fs::relative(entry.path(), root, error).generic_wstring());
    }
    std::sort(files.begin(), files.end());
    std::string descriptor;
    for (const auto& relative : files) {
        const auto absolute = root / fs::path(relative);
        const auto hash = Sha256File(absolute);
        if (!hash) return {};
        descriptor += WideToUtf8(relative);
        descriptor += "\n";
        descriptor += WideToUtf8(*hash);
        descriptor += "\n";
    }
    const auto result = Sha256Text(descriptor);
    return result.value_or(L"");
}

bool CopyTree(const fs::path& source, const fs::path& destination, StatusWindow& status) {
    try {
        fs::create_directories(destination);
        std::error_code error;
        for (const auto& entry : fs::recursive_directory_iterator(source, error)) {
            if (error) return false;
            const auto relative = fs::relative(entry.path(), source, error);
            if (error) return false;
            const auto target = destination / relative;
            if (entry.is_directory(error)) {
                fs::create_directories(target, error);
                if (error) return false;
                continue;
            }
            if (!entry.is_regular_file(error)) continue;
            fs::create_directories(target.parent_path(), error);
            if (error) return false;
            status.SetStatus(L"正在准备 Core：" + relative.wstring());
            fs::copy_file(entry.path(), target, fs::copy_options::overwrite_existing, error);
            if (error) return false;
        }
        return true;
    } catch (...) {
        return false;
    }
}

std::wstring UtcTimestamp() {
    SYSTEMTIME time{};
    GetSystemTime(&time);
    wchar_t buffer[64]{};
    swprintf_s(
        buffer,
        L"%04u-%02u-%02uT%02u:%02u:%02u.%03uZ",
        time.wYear,
        time.wMonth,
        time.wDay,
        time.wHour,
        time.wMinute,
        time.wSecond,
        time.wMilliseconds);
    return buffer;
}

std::wstring CorrelationId() {
    std::wostringstream value;
    value << L"boot-" << GetCurrentProcessId() << L"-" << GetTickCount64();
    return value.str();
}

void AppendLog(const fs::path& root, const std::wstring& event, const std::wstring& correlation) {
    try {
        const auto logDirectory = root / L".facm" / L"logs";
        fs::create_directories(logDirectory);
        std::ofstream output(logDirectory / L"bootstrapper.jsonl", std::ios::app | std::ios::binary);
        if (!output) return;
        output << "{\"ts\":\"" << EscapeJson(UtcTimestamp())
               << "\",\"event\":\"" << EscapeJson(event)
               << "\",\"correlationId\":\"" << EscapeJson(correlation) << "\"}\n";
    } catch (...) {
    }
}

void ErrorMessage(const std::wstring& title, const std::wstring& message) {
    if (g_suppressUi) return;
    MessageBoxW(nullptr, message.c_str(), title.c_str(), MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
}

bool ValidVersion(const std::wstring& version) {
    if (version.empty() || version.size() > 80) return false;
    return std::all_of(version.begin(), version.end(), [](wchar_t character) {
        return (character >= L'a' && character <= L'z') ||
               (character >= L'A' && character <= L'Z') ||
               (character >= L'0' && character <= L'9') ||
               character == L'.' || character == L'-' || character == L'_' ;
    });
}

std::wstring QuoteArgument(const std::wstring& value) {
    if (value.empty()) return L"\"\"";
    bool needsQuotes = value.find_first_of(L" \t\"") != std::wstring::npos;
    if (!needsQuotes) return value;
    std::wstring result = L"\"";
    size_t backslashes = 0;
    for (const auto character : value) {
        if (character == L'\\') {
            ++backslashes;
            continue;
        }
        if (character == L'"') {
            result.append(backslashes * 2 + 1, L'\\');
            result.push_back(L'"');
            backslashes = 0;
            continue;
        }
        result.append(backslashes, L'\\');
        backslashes = 0;
        result.push_back(character);
    }
    result.append(backslashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}

std::vector<std::wstring> CommandLineArguments() {
    int count = 0;
    auto* values = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!values) return {};
    std::vector<std::wstring> result;
    for (int index = 0; index < count; ++index) result.emplace_back(values[index]);
    LocalFree(values);
    return result;
}

std::optional<std::wstring> OptionValue(const std::vector<std::wstring>& arguments, const std::wstring& name) {
    const auto prefix = name + L"=";
    for (size_t index = 1; index < arguments.size(); ++index) {
        if (arguments[index].rfind(prefix, 0) == 0) return arguments[index].substr(prefix.size());
        if (arguments[index] == name && index + 1 < arguments.size()) return arguments[index + 1];
    }
    return std::nullopt;
}

bool HasOption(const std::vector<std::wstring>& arguments, const std::wstring& name) {
    return std::any_of(arguments.begin() + (arguments.empty() ? 0 : 1), arguments.end(), [&](const auto& argument) {
        return argument == name;
    });
}

bool IsBootstrapOnlyArgument(const std::wstring& argument) {
    return argument == L"--self-test" || argument == L"--dry-run" || argument == L"--resolve-only" || argument == L"--no-ui" ||
           argument == L"--provision-source" || argument.rfind(L"--provision-source=", 0) == 0 ||
           argument == L"--provision-pack" || argument.rfind(L"--provision-pack=", 0) == 0 ||
           argument == L"--manifest" || argument.rfind(L"--manifest=", 0) == 0 ||
           argument == L"--activate-version" || argument.rfind(L"--activate-version=", 0) == 0 ||
           argument == L"--verify-pack" || argument.rfind(L"--verify-pack=", 0) == 0 ||
           argument == L"--version" || argument.rfind(L"--version=", 0) == 0;
}

bool TakesBootstrapValue(const std::wstring& argument) {
    return argument == L"--provision-source" || argument == L"--provision-pack" ||
           argument == L"--manifest" || argument == L"--activate-version" ||
           argument == L"--verify-pack" || argument == L"--version";
}

fs::path StatePath(const fs::path& root) {
    return root / L".facm" / L"state" / kStateFileName;
}

bool WriteActiveState(const fs::path& root, const ActiveState& state) {
    return AtomicWrite(StatePath(root), ActiveStateJson(state));
}

bool VerifyPack(const fs::path& pack, const fs::path& manifestPath, std::wstring& failure) {
    const auto manifest = ReadComponentManifest(manifestPath);
    if (!manifest) {
        failure = L"组件清单缺失或格式无效。";
        return false;
    }
    if (manifest->componentId != kCoreComponent || manifest->architecture != L"win-x64" || !IsHexSha256(manifest->sha256)) {
        failure = L"组件清单的组件 ID、架构或 SHA-256 无效。";
        return false;
    }
    std::error_code error;
    if (!fs::is_regular_file(pack, error)) {
        failure = L"组件包不存在。";
        return false;
    }
    if (fs::file_size(pack, error) != manifest->packageSize || error) {
        failure = L"组件包大小与清单不一致。";
        return false;
    }
    const auto actual = Sha256File(pack);
    if (!actual || _wcsicmp(actual->c_str(), manifest->sha256.c_str()) != 0) {
        failure = L"组件包 SHA-256 校验失败。";
        return false;
    }
    return true;
}

bool ProvisionFromSource(
    const fs::path& root,
    const fs::path& source,
    const std::wstring& version,
    const std::optional<fs::path>& pack,
    const std::optional<fs::path>& suppliedManifest,
    StatusWindow& status,
    std::wstring& failure) {
    if (!ValidVersion(version)) {
        failure = L"版本号无效。";
        return false;
    }
    std::error_code error;
    if (!fs::is_directory(source, error)) {
        failure = L"本地 Core 源目录不存在。";
        return false;
    }
    if (pack) {
        const auto manifestPath = suppliedManifest.value_or(source / L"component.manifest.json");
        if (!VerifyPack(*pack, manifestPath, failure)) return false;
    }

    const auto staging = root / L".facm" / L"staging" / (std::wstring(kCoreComponent) + L"-" + version);
    const auto destination = root / L".facm" / L"versions" / version;
    if (!IsPathInside(root / L".facm" / L"staging", staging) || !IsPathInside(root / L".facm" / L"versions", destination)) {
        failure = L"暂存或版本路径越界。";
        return false;
    }
    if (fs::exists(destination, error)) {
        failure = L"目标版本已经存在，Boot-1 不覆盖已安装版本。";
        return false;
    }
    fs::remove_all(staging, error);
    if (error) {
        failure = L"无法清理暂存目录。";
        return false;
    }
    status.SetStatus(L"正在暂存 Core 文件…");
    if (!CopyTree(source, staging, status)) {
        fs::remove_all(staging, error);
        failure = L"Core 暂存失败；当前 active 版本未修改。";
        return false;
    }
    const auto entry = staging / kCoreEntryPoint;
    if (!fs::is_regular_file(entry, error) || fs::file_size(entry, error) == 0 ||
        !fs::is_regular_file(staging / L"FACM.App.dll", error)) {
        fs::remove_all(staging, error);
        failure = L"暂存 Core 缺少 FACM.App 入口或托管程序集；当前 active 版本未修改。";
        return false;
    }

    const auto installedSize = DirectorySize(staging);
    const auto digest = DirectoryDigest(staging);
    if (digest.empty()) {
        fs::remove_all(staging, error);
        failure = L"无法生成 Core 完整性摘要；当前 active 版本未修改。";
        return false;
    }
    const auto manifestDirectory = root / L".facm" / L"components" / kCoreComponent;
    fs::create_directories(manifestDirectory, error);
    if (error) {
        fs::remove_all(staging, error);
        failure = L"无法创建组件清单目录；当前 active 版本未修改。";
        return false;
    }
    std::ostringstream manifestJson;
    manifestJson << "{\n"
                 << "  \"schemaVersion\": 1,\n"
                 << "  \"componentId\": \"" << EscapeJson(kCoreComponent) << "\",\n"
                 << "  \"version\": \"" << EscapeJson(version) << "\",\n"
                 << "  \"architecture\": \"win-x64\",\n"
                 << "  \"required\": true,\n"
                 << "  \"packageSize\": " << installedSize << ",\n"
                 << "  \"installedSize\": " << installedSize << ",\n"
                 << "  \"sha256\": \"" << EscapeJson(digest) << "\",\n"
                 << "  \"entryPoint\": \"FACM.App.exe\",\n"
                 << "  \"dependencies\": []\n"
                 << "}\n";
    if (!AtomicWrite(manifestDirectory / (version + L".manifest.json"), manifestJson.str())) {
        fs::remove_all(staging, error);
        failure = L"组件清单写入失败；当前 active 版本未修改。";
        return false;
    }
    status.SetStatus(L"正在激活 Core 版本…");
    fs::create_directories(destination.parent_path(), error);
    if (error || !MoveFileExW(staging.c_str(), destination.c_str(), MOVEFILE_WRITE_THROUGH)) {
        fs::remove_all(staging, error);
        failure = L"Core 激活失败；当前 active 版本未修改。";
        return false;
    }

    const auto previous = ReadActiveState(StatePath(root));
    ActiveState next;
    next.activeVersion = version;
    next.activePath = (fs::path(L".facm") / L"versions" / version).generic_wstring();
    next.previousVersion = previous ? previous->activeVersion : L"";
    if (!WriteActiveState(root, next)) {
        failure = L"active.json 原子写入失败；已安装目录保留，已知 active 版本未被删除。";
        return false;
    }
    return true;
}

bool ActivateVersion(const fs::path& root, const std::wstring& version, std::wstring& failure) {
    if (!ValidVersion(version)) {
        failure = L"版本号无效。";
        return false;
    }
    const auto destination = root / L".facm" / L"versions" / version;
    std::error_code error;
    if (!fs::is_regular_file(destination / kCoreEntryPoint, error)) {
        failure = L"目标 Core 版本缺少入口文件；active 版本未修改。";
        return false;
    }
    const auto previous = ReadActiveState(StatePath(root));
    ActiveState next;
    next.activeVersion = version;
    next.activePath = (fs::path(L".facm") / L"versions" / version).generic_wstring();
    next.previousVersion = previous ? previous->activeVersion : L"";
    if (!WriteActiveState(root, next)) {
        failure = L"active.json 原子写入失败；active 版本未修改。";
        return false;
    }
    return true;
}

int LaunchActive(const fs::path& root, const std::vector<std::wstring>& arguments, const std::wstring& correlation) {
    const auto state = ReadActiveState(StatePath(root));
    if (!state) {
        ErrorMessage(L"FACM", L"未找到有效的 FACM Core。请先使用本地组件源完成一次初始化。");
        return 2;
    }
    const auto activeDirectory = root / fs::path(state->activePath);
    if (!IsPathInside(root / L".facm" / L"versions", activeDirectory)) {
        ErrorMessage(L"FACM", L"active.json 的 Core 路径不在受控版本目录内。");
        return 3;
    }
    const auto executable = activeDirectory / kCoreEntryPoint;
    std::error_code error;
    if (!fs::is_regular_file(executable, error)) {
        ErrorMessage(L"FACM", L"当前 active Core 不完整，未修改已知版本状态。");
        return 4;
    }
    if (HasOption(arguments, L"--resolve-only") || HasOption(arguments, L"--dry-run")) return 0;

    SetEnvironmentVariableW(L"FACM_ROOT", root.c_str());
    const auto dataRoot = root / L".facm";
    SetEnvironmentVariableW(L"FACM_DATA_ROOT", dataRoot.c_str());
    SetEnvironmentVariableW(kCorrelationEnvironment, correlation.c_str());
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_SYSTEM32);

    std::wstring commandLine = QuoteArgument(executable.wstring());
    for (size_t index = 1; index < arguments.size(); ++index) {
        if (IsBootstrapOnlyArgument(arguments[index])) {
            if (TakesBootstrapValue(arguments[index]) && arguments[index].find(L'=') == std::wstring::npos)
                ++index;
            continue;
        }
        commandLine += L" ";
        commandLine += QuoteArgument(arguments[index]);
    }
    std::vector<wchar_t> mutableCommand(commandLine.begin(), commandLine.end());
    mutableCommand.push_back(L'\0');

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    AppendLog(root, L"facm-process-create-requested", correlation);
    const BOOL created = CreateProcessW(
        executable.c_str(),
        mutableCommand.data(),
        nullptr,
        nullptr,
        FALSE,
        CREATE_UNICODE_ENVIRONMENT,
        nullptr,
        activeDirectory.c_str(),
        &startup,
        &process);
    if (!created) {
        AppendLog(root, L"facm-process-create-failed", correlation);
        ErrorMessage(L"FACM", L"无法启动当前 FACM Core。请检查版本目录和本地运行时文件。");
        return 5;
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    auto updated = *state;
    updated.lastSuccessfulLaunch = UtcTimestamp();
    WriteActiveState(root, updated);
    AppendLog(root, L"facm-process-created", correlation);
    return 0;
}

} // namespace

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    const auto arguments = CommandLineArguments();
    const auto module = GetModulePath();
    if (arguments.empty() || module.empty()) return 1;
    g_suppressUi = HasOption(arguments, L"--no-ui");
    const fs::path root = fs::path(module).parent_path();
    const auto correlation = CorrelationId();
    AppendLog(root, L"bootstrap-process-start", correlation);

    HANDLE mutex = CreateMutexW(nullptr, FALSE, L"Local\\FACM-Bootstrapper-2C429A53-6710-48BC-A57C-32BEA688B25D");
    if (mutex && GetLastError() == ERROR_ALREADY_EXISTS) {
        CloseHandle(mutex);
        return 0;
    }

    if (HasOption(arguments, L"--self-test")) {
        const auto staging = root / L".facm" / L"staging";
        const auto versions = root / L".facm" / L"versions";
        return IsPathInside(root / L".facm", staging) && IsPathInside(root / L".facm", versions) ? 0 : 10;
    }

    const auto verifyPack = OptionValue(arguments, L"--verify-pack");
    if (verifyPack) {
        const auto manifest = OptionValue(arguments, L"--manifest");
        std::wstring failure;
        const auto success = manifest && VerifyPack(*verifyPack, *manifest, failure);
        if (!success && !failure.empty()) ErrorMessage(L"FACM Core 校验", failure);
        if (mutex) CloseHandle(mutex);
        return success ? 0 : 11;
    }

    StatusWindow status;
    const auto source = OptionValue(arguments, L"--provision-source");
    if (source) {
        status.Show(L"正在准备 FACM Core…");
        const auto version = OptionValue(arguments, L"--version");
        const auto pack = OptionValue(arguments, L"--provision-pack");
        const auto manifest = OptionValue(arguments, L"--manifest");
        std::wstring failure;
        const auto success = version && ProvisionFromSource(
            root,
            *source,
            *version,
            pack ? std::optional<fs::path>(*pack) : std::nullopt,
            manifest ? std::optional<fs::path>(*manifest) : std::nullopt,
            status,
            failure);
        status.Close();
        if (!success) {
            if (mutex) CloseHandle(mutex);
            ErrorMessage(L"FACM Core 初始化", failure.empty() ? L"本地 Core 初始化失败。" : failure);
            return 12;
        }
        if (HasOption(arguments, L"--dry-run")) {
            if (mutex) CloseHandle(mutex);
            return 0;
        }
    }

    const auto activation = OptionValue(arguments, L"--activate-version");
    if (activation) {
        std::wstring failure;
        if (!ActivateVersion(root, *activation, failure)) {
            if (mutex) CloseHandle(mutex);
            ErrorMessage(L"FACM Core 版本切换", failure);
            return 13;
        }
    }

    const auto result = LaunchActive(root, arguments, correlation);
    AppendLog(root, L"bootstrap-process-exit", correlation);
    if (mutex) CloseHandle(mutex);
    return result;
}
