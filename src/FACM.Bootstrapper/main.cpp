#include <windows.h>
#include <bcrypt.h>
#include <fdi.h>
#include <shellapi.h>
#include <winhttp.h>

#include <algorithm>
#include <chrono>
#include <cctype>
#include <cstdint>
#include <cwctype>
#include <cwchar>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <io.h>
#include <fcntl.h>
#include <map>
#include <optional>
#include <set>
#include <share.h>
#include <sstream>
#include <string>
#include <sys/stat.h>
#include <vector>

#include "ManifestTrust.h"

namespace fs = std::filesystem;

namespace {

constexpr wchar_t kCoreComponent[] = L"facm-core-win-x64";
constexpr wchar_t kCoreEntryPoint[] = L"FACM.App.exe";
constexpr wchar_t kStateFileName[] = L"active.json";
constexpr wchar_t kComponentsStateFileName[] = L"components.json";
constexpr wchar_t kBootstrapConfigFileName[] = L"bootstrap.json";
constexpr wchar_t kCorrelationEnvironment[] = L"FACM_BOOTSTRAP_CORRELATION_ID";
constexpr wchar_t kExpectedArchitecture[] = L"win-x64";
constexpr std::uintmax_t kMaximumComponentInstalledBytes = 1024ull * 1024ull * 1024ull;
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
    std::wstring packageFormat;
    std::wstring contentDigest;
    std::uintmax_t fileCount = 0;
    std::wstring primaryUrl;
    std::vector<std::wstring> mirrorUrls;
    std::vector<std::wstring> dependencies;
    std::wstring keyId;
    std::wstring componentManifestUrl;
    std::vector<std::wstring> componentManifestMirrors;
    std::wstring componentManifestSha256;
};

struct ApplicationManifest {
    int schemaVersion = 0;
    std::wstring applicationId;
    std::wstring applicationVersion;
    std::wstring architecture;
    std::wstring trustMode;
    std::wstring keyId;
    std::vector<std::wstring> manifestMirrors;
    std::vector<ComponentManifest> components;
};

struct InstalledComponent {
    std::wstring componentId;
    std::wstring version;
    std::wstring path;
    std::uintmax_t installedSize = 0;
    std::wstring contentDigest;
};

struct ComponentsState {
    int schemaVersion = 1;
    std::wstring applicationVersion;
    std::vector<InstalledComponent> components;
};

struct BootstrapConfig {
    std::wstring manifestUrl;
    std::vector<std::wstring> manifestMirrors;
    bool allowUnsignedLocal = false;
    bool allowInsecureLocal = false;
};

struct TransportCandidate {
    std::wstring id;
    std::wstring url;
    std::wstring sourceUrl;
    bool directGithubFallback = false;
};

struct GithubProxyPrefix {
    const wchar_t* id;
    const wchar_t* prefix;
};

constexpr GithubProxyPrefix kGithubProxyPrefixes[] = {
    {L"ghfast.top", L"https://ghfast.top/"},
    {L"gh-proxy.com", L"https://gh-proxy.com/"},
    {L"gh.llkk.cc", L"https://gh.llkk.cc/"},
};

class StatusWindow {
public:
    void Show(const std::wstring& text) {
        if (g_suppressUi) return;
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

std::optional<std::string> JsonArrayText(const std::string& json, const std::string& key) {
    const auto marker = "\"" + key + "\"";
    const auto keyPosition = json.find(marker);
    if (keyPosition == std::string::npos) return std::nullopt;
    auto open = keyPosition + marker.size();
    while (open < json.size() && isspace(static_cast<unsigned char>(json[open]))) ++open;
    if (open >= json.size() || json[open] != ':') return std::nullopt;
    ++open;
    while (open < json.size() && isspace(static_cast<unsigned char>(json[open]))) ++open;
    if (open >= json.size() || json[open] != '[') return std::nullopt;
    bool quoted = false;
    bool escaped = false;
    int depth = 0;
    for (size_t index = open; index < json.size(); ++index) {
        const auto character = json[index];
        if (quoted) {
            if (escaped) escaped = false;
            else if (character == '\\') escaped = true;
            else if (character == '"') quoted = false;
            continue;
        }
        if (character == '"') {
            quoted = true;
            continue;
        }
        if (character == '[') ++depth;
        if (character == ']' && --depth == 0) return json.substr(open + 1, index - open - 1);
    }
    return std::nullopt;
}

std::vector<std::string> JsonObjectArray(const std::string& json, const std::string& key) {
    const auto contents = JsonArrayText(json, key);
    if (!contents) return {};
    std::vector<std::string> objects;
    size_t start = std::string::npos;
    int depth = 0;
    bool quoted = false;
    bool escaped = false;
    for (size_t index = 0; index < contents->size(); ++index) {
        const auto character = (*contents)[index];
        if (quoted) {
            if (escaped) escaped = false;
            else if (character == '\\') escaped = true;
            else if (character == '"') quoted = false;
            continue;
        }
        if (character == '"') {
            quoted = true;
            continue;
        }
        if (character == '{') {
            if (depth == 0) start = index;
            ++depth;
        } else if (character == '}' && depth > 0) {
            --depth;
            if (depth == 0 && start != std::string::npos) {
                objects.push_back(contents->substr(start, index - start + 1));
                start = std::string::npos;
            }
        }
    }
    return objects;
}

std::vector<std::wstring> JsonStringArray(const std::string& json, const std::string& key) {
    const auto contents = JsonArrayText(json, key);
    if (!contents) return {};
    std::vector<std::wstring> values;
    size_t index = 0;
    while (index < contents->size()) {
        const auto quote = contents->find('"', index);
        if (quote == std::string::npos) break;
        std::string value;
        bool escaped = false;
        for (size_t cursor = quote + 1; cursor < contents->size(); ++cursor) {
            const auto character = (*contents)[cursor];
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
            if (character == '"') {
                values.push_back(Utf8ToWide(value));
                index = cursor + 1;
                break;
            }
            value.push_back(character);
        }
        if (index == quote + 1) break;
    }
    return values;
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

bool ValidVersion(const std::wstring& version);
bool ValidComponentId(const std::wstring& id);
bool IsHexSha256(const std::wstring& value);

bool IsSafeInstalledComponentPath(const std::wstring& value) {
    if (value.empty() || value.find(L':') != std::wstring::npos || value.front() == L'\\' || value.front() == L'/') {
        return false;
    }
    std::wstring normalized = value;
    std::replace(normalized.begin(), normalized.end(), L'\\', L'/');
    const auto parts = [&]() {
        std::vector<std::wstring> result;
        size_t start = 0;
        while (start <= normalized.size()) {
            const auto separator = normalized.find(L'/', start);
            const auto part = normalized.substr(start, separator == std::wstring::npos ? std::wstring::npos : separator - start);
            if (part.empty() || part == L"." || part == L"..") return std::vector<std::wstring>{};
            result.push_back(part);
            if (separator == std::wstring::npos) break;
            start = separator + 1;
        }
        return result;
    }();
    return parts.size() >= 4 && _wcsicmp(parts[0].c_str(), L".facm") == 0 &&
           _wcsicmp(parts[1].c_str(), L"components") == 0;
}

std::optional<ActiveState> ReadActiveState(const fs::path& path) {
    std::string json;
    if (!ReadText(path, json)) return std::nullopt;
    const auto schemaVersion = JsonUnsigned(json, "schemaVersion");
    const auto activeVersion = JsonString(json, "activeVersion");
    const auto activePath = JsonString(json, "activePath");
    if (!schemaVersion || *schemaVersion != 1 || !activeVersion || !activePath || activeVersion->empty() || activePath->empty()) return std::nullopt;
    ActiveState state;
    state.activeVersion = Utf8ToWide(*activeVersion);
    state.activePath = Utf8ToWide(*activePath);
    auto normalizedActivePath = state.activePath;
    std::replace(normalizedActivePath.begin(), normalizedActivePath.end(), L'\\', L'/');
    const auto endsWithParent = normalizedActivePath.size() >= 3 && normalizedActivePath.compare(normalizedActivePath.size() - 3, 3, L"/..") == 0;
    if (!ValidVersion(state.activeVersion) || normalizedActivePath.find(L".facm/versions/") != 0 ||
        normalizedActivePath.find(L"/../") != std::wstring::npos || endsWithParent) return std::nullopt;
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

std::optional<ComponentManifest> ParseComponentManifest(const std::string& json) {
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
    manifest.schemaVersion = static_cast<int>(JsonUnsigned(json, "schemaVersion").value_or(1));
    manifest.componentId = Utf8ToWide(*componentId);
    manifest.version = Utf8ToWide(*version);
    manifest.architecture = Utf8ToWide(*architecture);
    manifest.packageSize = *packageSize;
    manifest.installedSize = *installedSize;
    manifest.sha256 = Utf8ToWide(*sha256);
    manifest.entryPoint = Utf8ToWide(*entryPoint);
    manifest.required = JsonBool(json, "required").value_or(true);
    manifest.packageFormat = Utf8ToWide(JsonString(json, "packageFormat").value_or("zip"));
    manifest.contentDigest = Utf8ToWide(JsonString(json, "contentDigest").value_or(""));
    manifest.fileCount = JsonUnsigned(json, "fileCount").value_or(0);
    manifest.primaryUrl = Utf8ToWide(JsonString(json, "primaryUrl").value_or(""));
    manifest.mirrorUrls = JsonStringArray(json, "mirrors");
    manifest.dependencies = JsonStringArray(json, "dependencies");
    manifest.keyId = Utf8ToWide(JsonString(json, "keyId").value_or(""));
    manifest.componentManifestUrl = Utf8ToWide(JsonString(json, "componentManifestUrl").value_or(""));
    manifest.componentManifestMirrors = JsonStringArray(json, "componentManifestMirrors");
    manifest.componentManifestSha256 = Utf8ToWide(JsonString(json, "componentManifestSha256").value_or(""));
    return manifest;
}

std::optional<ComponentManifest> ReadComponentManifest(const fs::path& path) {
    std::string json;
    if (!ReadText(path, json)) return std::nullopt;
    return ParseComponentManifest(json);
}

std::optional<ApplicationManifest> ReadApplicationManifest(const std::string& json) {
    const auto schema = JsonUnsigned(json, "schemaVersion");
    const auto applicationId = JsonString(json, "applicationId");
    const auto applicationVersion = JsonString(json, "applicationVersion");
    const auto architecture = JsonString(json, "architecture");
    const auto trustMode = JsonString(json, "trustMode");
    if (!schema || !applicationId || !applicationVersion || !architecture || !trustMode) return std::nullopt;
    ApplicationManifest manifest;
    manifest.schemaVersion = static_cast<int>(*schema);
    manifest.applicationId = Utf8ToWide(*applicationId);
    manifest.applicationVersion = Utf8ToWide(*applicationVersion);
    manifest.architecture = Utf8ToWide(*architecture);
    manifest.trustMode = Utf8ToWide(*trustMode);
    manifest.keyId = Utf8ToWide(JsonString(json, "keyId").value_or(""));
    manifest.manifestMirrors = JsonStringArray(json, "manifestMirrors");
    for (const auto& object : JsonObjectArray(json, "components")) {
        const auto component = ParseComponentManifest(object);
        if (!component) return std::nullopt;
        manifest.components.push_back(*component);
    }
    if (manifest.components.empty()) return std::nullopt;
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
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    std::vector<UCHAR> object;
    std::vector<UCHAR> digest(32);
    DWORD objectLength = 0;
    DWORD resultLength = 0;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0 ||
        BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH, reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &resultLength, 0) < 0) {
        if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
        return std::nullopt;
    }
    object.resize(objectLength);
    if (BCryptCreateHash(algorithm, &hash, object.data(), objectLength, nullptr, 0, 0) < 0 ||
        (value.size() > 0 && BCryptHashData(hash, reinterpret_cast<PUCHAR>(const_cast<char*>(value.data())), static_cast<ULONG>(value.size()), 0) < 0) ||
        BCryptFinishHash(hash, digest.data(), static_cast<ULONG>(digest.size()), 0) < 0) {
        if (hash) BCryptDestroyHash(hash);
        BCryptCloseAlgorithmProvider(algorithm, 0);
        return std::nullopt;
    }
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    std::wostringstream output;
    output << std::hex << std::setfill(L'0');
    for (const auto byte : digest) output << std::setw(2) << static_cast<unsigned int>(byte);
    return output.str();
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

void AppendLogDetail(const fs::path& root, const std::wstring& event, const std::wstring& correlation, const std::wstring& detail) {
    try {
        const auto logDirectory = root / L".facm" / L"logs";
        fs::create_directories(logDirectory);
        std::ofstream output(logDirectory / L"bootstrapper.jsonl", std::ios::app | std::ios::binary);
        if (!output) return;
        output << "{\"ts\":\"" << EscapeJson(UtcTimestamp())
               << "\",\"event\":\"" << EscapeJson(event)
               << "\",\"correlationId\":\"" << EscapeJson(correlation)
               << "\",\"detail\":\"" << EscapeJson(detail) << "\"}\n";
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

bool ParseNumericVersionBase(const std::wstring& base, std::vector<std::uintmax_t>& parts) {
    if (base.empty()) return false;
    std::uintmax_t value = 0;
    bool hasDigit = false;
    for (size_t index = 0; index <= base.size(); ++index) {
        if (index == base.size() || base[index] == L'.') {
            if (!hasDigit) return false;
            parts.push_back(value);
            value = 0;
            hasDigit = false;
            continue;
        }
        if (base[index] < L'0' || base[index] > L'9') return false;
        hasDigit = true;
        const auto digit = static_cast<std::uintmax_t>(base[index] - L'0');
        if (value > (static_cast<std::uintmax_t>(-1) - digit) / 10) return false;
        value = value * 10 + digit;
    }
    return !parts.empty();
}

int CompareReleaseVersions(const std::wstring& left, const std::wstring& right) {
    const auto leftSeparator = left.find_first_of(L"-_");
    const auto rightSeparator = right.find_first_of(L"-_");
    const auto leftBase = left.substr(0, leftSeparator);
    const auto rightBase = right.substr(0, rightSeparator);
    const auto leftSuffix = leftSeparator == std::wstring::npos ? L"" : left.substr(leftSeparator + 1);
    const auto rightSuffix = rightSeparator == std::wstring::npos ? L"" : right.substr(rightSeparator + 1);
    std::vector<std::uintmax_t> leftParts;
    std::vector<std::uintmax_t> rightParts;
    if (!ParseNumericVersionBase(leftBase, leftParts) || !ParseNumericVersionBase(rightBase, rightParts)) {
        return _wcsicmp(left.c_str(), right.c_str());
    }
    const auto count = std::max(leftParts.size(), rightParts.size());
    for (size_t index = 0; index < count; ++index) {
        const auto leftPart = index < leftParts.size() ? leftParts[index] : 0;
        const auto rightPart = index < rightParts.size() ? rightParts[index] : 0;
        if (leftPart < rightPart) return -1;
        if (leftPart > rightPart) return 1;
    }
    if (leftSuffix.empty() != rightSuffix.empty()) return leftSuffix.empty() ? 1 : -1;
    return _wcsicmp(leftSuffix.c_str(), rightSuffix.c_str());
}

bool ValidComponentId(const std::wstring& id) {
    if (id.empty() || id.size() > 80) return false;
    return std::all_of(id.begin(), id.end(), [](wchar_t character) {
        return (character >= L'a' && character <= L'z') ||
               (character >= L'A' && character <= L'Z') ||
               (character >= L'0' && character <= L'9') ||
               character == L'.' || character == L'-' || character == L'_';
    });
}

bool IsSafeArchivePath(const std::string& archiveName, fs::path& relative) {
    const auto wide = Utf8ToWide(archiveName);
    if (wide.empty() || wide.find(L':') != std::wstring::npos || wide.front() == L'\\' || wide.front() == L'/') return false;
    std::wstring normalized = wide;
    std::replace(normalized.begin(), normalized.end(), L'/', L'\\');
    size_t start = 0;
    while (start <= normalized.size()) {
        const auto separator = normalized.find(L'\\', start);
        const auto part = normalized.substr(start, separator == std::wstring::npos ? std::wstring::npos : separator - start);
        if (part.empty() || part == L"." || part == L"..") return false;
        start = separator == std::wstring::npos ? normalized.size() + 1 : separator + 1;
    }
    relative = fs::path(normalized);
    return !relative.empty() && relative.is_relative();
}

bool IsLocalDevelopmentUrl(const std::wstring& url) {
    const auto lower = [&]() {
        auto copy = url;
        std::transform(copy.begin(), copy.end(), copy.begin(), towlower);
        return copy;
    }();
    return lower.rfind(L"http://127.0.0.1:", 0) == 0 ||
           lower.rfind(L"http://localhost:", 0) == 0 ||
           lower.rfind(L"http://[::1]:", 0) == 0;
}

bool IsHttpsUrl(const std::wstring& url) {
    auto lower = url;
    std::transform(lower.begin(), lower.end(), lower.begin(), towlower);
    return lower.rfind(L"https://", 0) == 0;
}

std::wstring DetachedSignatureUrl(const std::wstring& url) {
    return url + L".sig";
}

bool IsAllowedUrl(const std::wstring& url, bool allowInsecureLocal) {
    if (IsHttpsUrl(url)) return true;
    return allowInsecureLocal && IsLocalDevelopmentUrl(url);
}

std::wstring ComponentPackageExtension(const ComponentManifest& component) {
    auto format = component.packageFormat;
    std::transform(format.begin(), format.end(), format.begin(), towlower);
    if (format == L"cab") return L".cab";
    if (format == L"zip") return L".zip";
    return {};
}

bool IsReparsePoint(const fs::path& path) {
    const auto attributes = GetFileAttributesW(path.c_str());
    return attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
}

struct ExtractionContext {
    fs::path destination;
    std::set<std::wstring> files;
    std::uintmax_t writtenBytes = 0;
    std::uintmax_t maximumBytes = kMaximumComponentInstalledBytes;
    bool failed = false;
    std::string failureDetail;
};

thread_local ExtractionContext* g_extractionContext = nullptr;

void* DIAMONDAPI CabAlloc(ULONG bytes) {
    return std::malloc(bytes);
}

void DIAMONDAPI CabFree(void* value) {
    std::free(value);
}

INT_PTR DIAMONDAPI CabOpen(char* file, int flags, int mode) {
    int descriptor = -1;
    if (!file || _sopen_s(&descriptor, file, flags | _O_BINARY, _SH_DENYNO, mode) != 0) {
        if (g_extractionContext) g_extractionContext->failureDetail = std::string("input open failed: ") + (file ? file : "(null)");
        return -1;
    }
    return static_cast<INT_PTR>(descriptor);
}

UINT DIAMONDAPI CabRead(INT_PTR descriptor, void* buffer, UINT bytes) {
    if (descriptor < 0) return 0;
    const auto read = _read(static_cast<int>(descriptor), buffer, bytes);
    if (read < 0 && g_extractionContext) {
        g_extractionContext->failed = true;
        g_extractionContext->failureDetail = "input read failed";
    }
    return read < 0 ? 0 : static_cast<UINT>(read);
}

UINT DIAMONDAPI CabWrite(INT_PTR descriptor, void* buffer, UINT bytes) {
    if (descriptor < 0 || !g_extractionContext) return 0;
    if (bytes > g_extractionContext->maximumBytes - std::min(g_extractionContext->maximumBytes, g_extractionContext->writtenBytes)) {
        g_extractionContext->failed = true;
        return 0;
    }
    const auto written = _write(static_cast<int>(descriptor), buffer, bytes);
    if (written < 0) {
        g_extractionContext->failed = true;
        return 0;
    }
    g_extractionContext->writtenBytes += static_cast<std::uintmax_t>(written);
    return static_cast<UINT>(written);
}

int DIAMONDAPI CabClose(INT_PTR descriptor) {
    return descriptor < 0 ? -1 : _close(static_cast<int>(descriptor));
}

LONG DIAMONDAPI CabSeek(INT_PTR descriptor, LONG distance, int seekType) {
    if (descriptor < 0) return -1;
    return _lseek(static_cast<int>(descriptor), distance, seekType);
}

INT_PTR DIAMONDAPI CabNotify(FDINOTIFICATIONTYPE type, PFDINOTIFICATION notification) {
    if (!g_extractionContext || !notification) return -1;
    if (type == fdintCOPY_FILE) {
        fs::path relative;
        if (!IsSafeArchivePath(notification->psz1 ? notification->psz1 : "", relative)) {
            g_extractionContext->failed = true;
            g_extractionContext->failureDetail = notification->psz1 ? notification->psz1 : "invalid archive path";
            return -1;
        }
        const auto key = relative.generic_wstring();
        if (!g_extractionContext->files.insert(key).second) {
            g_extractionContext->failed = true;
            g_extractionContext->failureDetail = "duplicate archive path: " + WideToUtf8(key);
            return -1;
        }
        const auto target = g_extractionContext->destination / relative;
        if (!IsPathInside(g_extractionContext->destination, target) || IsReparsePoint(target)) {
            g_extractionContext->failed = true;
            g_extractionContext->failureDetail = "archive path escaped destination";
            return -1;
        }
        std::error_code error;
        fs::create_directories(target.parent_path(), error);
        if (error || IsReparsePoint(target.parent_path())) {
            g_extractionContext->failed = true;
            g_extractionContext->failureDetail = "parent directory rejected";
            return -1;
        }
        int descriptor = -1;
        if (_wsopen_s(&descriptor, target.c_str(), _O_WRONLY | _O_CREAT | _O_TRUNC | _O_BINARY, _SH_DENYNO, _S_IREAD | _S_IWRITE) != 0) {
            g_extractionContext->failed = true;
            g_extractionContext->failureDetail = "target file open failed";
            return -1;
        }
        return static_cast<INT_PTR>(descriptor);
    }
    if (type == fdintCLOSE_FILE_INFO) return CabClose(notification->hf) == 0 ? 1 : 0;
    if (type == fdintPARTIAL_FILE) {
        return 0;
    }
    if (type == fdintNEXT_CABINET) {
        g_extractionContext->failed = true;
        g_extractionContext->failureDetail = "multi-cabinet input is not supported";
        return -1;
    }
    return 0;
}

bool ExtractCab(const fs::path& pack, const fs::path& destination, const ComponentManifest& manifest, StatusWindow& status, std::wstring& failure) {
    if (manifest.packageFormat != L"cab" || !IsPathInside(destination.parent_path(), destination)) {
        failure = L"组件包格式或解包目标无效。";
        return false;
    }
    std::error_code error;
    fs::create_directories(destination, error);
    if (error) {
        failure = L"无法创建组件暂存目录。";
        return false;
    }
    ExtractionContext context;
    context.destination = destination;
    context.maximumBytes = std::min(kMaximumComponentInstalledBytes, std::max<std::uintmax_t>(manifest.installedSize, 1));
    ERF erf{};
    const auto hfdi = FDICreate(CabAlloc, CabFree, CabOpen, CabRead, CabWrite, CabClose, CabSeek, cpu80386, &erf);
    if (!hfdi) {
        failure = L"Windows Cabinet 解包器初始化失败。";
        return false;
    }
    auto cabinet = WideToUtf8(pack.filename().wstring());
    auto cabinetPath = WideToUtf8(pack.parent_path().wstring());
    if (cabinetPath.empty() || cabinet.empty()) {
        FDIDestroy(hfdi);
        failure = L"组件包路径无法转换为本地编码。";
        return false;
    }
    if (cabinetPath.back() != '\\') cabinetPath.push_back('\\');
    g_extractionContext = &context;
    status.SetStatus(L"正在解包组件：" + manifest.componentId);
    const auto copied = FDICopy(hfdi, cabinet.data(), cabinetPath.data(), 0, CabNotify, nullptr, nullptr);
    g_extractionContext = nullptr;
    FDIDestroy(hfdi);
    if (!copied || context.failed) {
        failure = L"组件 CAB 解包失败或触发了安全限制（FDI " + std::to_wstring(erf.erfOper) + L":" + std::to_wstring(erf.erfType) + L"）。";
        if (!context.failureDetail.empty()) failure += L" " + Utf8ToWide(context.failureDetail);
        return false;
    }
    if (context.files.empty() || context.writtenBytes != manifest.installedSize ||
        (manifest.fileCount != 0 && context.files.size() != manifest.fileCount)) {
        failure = L"组件解包后的文件数量或大小与清单不一致（实际文件 " + std::to_wstring(context.files.size()) +
                  L"，清单 " + std::to_wstring(manifest.fileCount) + L"；实际字节 " + std::to_wstring(context.writtenBytes) +
                  L"，清单 " + std::to_wstring(manifest.installedSize) + L"）。";
        return false;
    }
    const auto digest = DirectoryDigest(destination);
    if (manifest.contentDigest.empty() || digest.empty() || _wcsicmp(digest.c_str(), manifest.contentDigest.c_str()) != 0) {
        failure = L"组件解包后的内容摘要与清单不一致（实际 " + digest + L"，清单 " + manifest.contentDigest + L"）。";
        return false;
    }
    return true;
}

struct HttpResponse {
    DWORD status = 0;
    std::uintmax_t contentLength = 0;
    bool hasContentLength = false;
    std::wstring location;
    bool hasContentRange = false;
    std::uintmax_t rangeStart = 0;
    std::uintmax_t rangeEnd = 0;
    std::uintmax_t rangeTotal = 0;
};

bool CrackHttpUrl(const std::wstring& url, URL_COMPONENTS& components, std::vector<wchar_t>& host, std::vector<wchar_t>& path, std::vector<wchar_t>& extra, std::wstring& failure) {
    std::wstring mutableUrl = url;
    host.assign(512, L'\0');
    path.assign(32768, L'\0');
    extra.assign(8192, L'\0');
    components = {};
    components.dwStructSize = sizeof(components);
    components.lpszHostName = host.data();
    components.dwHostNameLength = static_cast<DWORD>(host.size() - 1);
    components.lpszUrlPath = path.data();
    components.dwUrlPathLength = static_cast<DWORD>(path.size() - 1);
    components.lpszExtraInfo = extra.data();
    components.dwExtraInfoLength = static_cast<DWORD>(extra.size() - 1);
    if (!WinHttpCrackUrl(mutableUrl.data(), 0, 0, &components)) {
        failure = L"组件地址解析失败。";
        return false;
    }
    if (components.dwHostNameLength == 0 || components.dwUrlPathLength == 0) {
        failure = L"组件地址缺少主机或路径。";
        return false;
    }
    return true;
}

std::wstring Lowercase(std::wstring value) {
    std::transform(value.begin(), value.end(), value.begin(), towlower);
    return value;
}

bool IsCanonicalGithubReleaseUrl(const std::wstring& url) {
    URL_COMPONENTS components{};
    std::vector<wchar_t> host;
    std::vector<wchar_t> path;
    std::vector<wchar_t> extra;
    std::wstring failure;
    if (!CrackHttpUrl(url, components, host, path, extra, failure) ||
        components.nScheme != INTERNET_SCHEME_HTTPS ||
        Lowercase(std::wstring(host.data(), components.dwHostNameLength)) != L"github.com" ||
        components.dwExtraInfoLength != 0) {
        return false;
    }
    const auto pathText = std::wstring(path.data(), components.dwUrlPathLength);
    if (pathText.empty() || pathText.front() != L'/' || pathText.find(L"//") != std::wstring::npos ||
        pathText.find(L"..") != std::wstring::npos) {
        return false;
    }
    std::vector<std::wstring> segments;
    size_t start = 1;
    while (start <= pathText.size()) {
        const auto separator = pathText.find(L'/', start);
        segments.push_back(pathText.substr(start, separator == std::wstring::npos ? std::wstring::npos : separator - start));
        if (separator == std::wstring::npos) break;
        start = separator + 1;
    }
    return segments.size() == 6 && !segments[0].empty() && !segments[1].empty() &&
           Lowercase(segments[2]) == L"releases" && Lowercase(segments[3]) == L"download" &&
           !segments[4].empty() && !segments[5].empty();
}

bool IsApprovedGithubRedirect(const std::wstring& url) {
    if (!IsHttpsUrl(url) || url.find(L'@') != std::wstring::npos) return false;
    URL_COMPONENTS components{};
    std::vector<wchar_t> host;
    std::vector<wchar_t> path;
    std::vector<wchar_t> extra;
    std::wstring failure;
    if (!CrackHttpUrl(url, components, host, path, extra, failure)) return false;
    const auto hostName = Lowercase(std::wstring(host.data(), components.dwHostNameLength));
    if (hostName == L"github.com") return IsCanonicalGithubReleaseUrl(url);
    return components.nScheme == INTERNET_SCHEME_HTTPS &&
           (hostName == L"release-assets.githubusercontent.com" || hostName == L"objects.githubusercontent.com");
}

std::vector<TransportCandidate> BuildTransportCandidates(const std::wstring& sourceUrl) {
    std::vector<TransportCandidate> candidates;
    if (!IsCanonicalGithubReleaseUrl(sourceUrl)) {
        candidates.push_back({L"direct", sourceUrl, sourceUrl, false});
        return candidates;
    }
    for (const auto& proxy : kGithubProxyPrefixes) {
        candidates.push_back({proxy.id, std::wstring(proxy.prefix) + sourceUrl, sourceUrl, false});
    }
    candidates.push_back({L"github-direct", sourceUrl, sourceUrl, true});
    return candidates;
}

std::vector<TransportCandidate> BuildOrderedTransportCandidates(const std::vector<std::wstring>& sourceUrls) {
    std::vector<TransportCandidate> candidates;
    std::set<std::wstring> seen;
    const auto add = [&](const TransportCandidate& candidate) {
        if (seen.insert(candidate.url).second) candidates.push_back(candidate);
    };
    for (const auto& sourceUrl : sourceUrls) {
        for (const auto& candidate : BuildTransportCandidates(sourceUrl)) {
            if (!candidate.directGithubFallback) add(candidate);
        }
    }
    for (const auto& sourceUrl : sourceUrls) {
        for (const auto& candidate : BuildTransportCandidates(sourceUrl)) {
            if (candidate.directGithubFallback) add(candidate);
        }
    }
    return candidates;
}

bool QueryHeaderText(HINTERNET request, DWORD query, std::wstring& value) {
    DWORD bytes = 0;
    if (WinHttpQueryHeaders(request, query, WINHTTP_HEADER_NAME_BY_INDEX, nullptr, &bytes, WINHTTP_NO_HEADER_INDEX) ||
        GetLastError() != ERROR_INSUFFICIENT_BUFFER || bytes < sizeof(wchar_t)) {
        return false;
    }
    std::vector<wchar_t> buffer(bytes / sizeof(wchar_t) + 1, L'\0');
    if (!WinHttpQueryHeaders(request, query, WINHTTP_HEADER_NAME_BY_INDEX, buffer.data(), &bytes, WINHTTP_NO_HEADER_INDEX)) {
        return false;
    }
    value.assign(buffer.data(), bytes / sizeof(wchar_t));
    while (!value.empty() && value.back() == L'\0') value.pop_back();
    return !value.empty();
}

bool ParseContentRange(const std::wstring& value, std::uintmax_t& start, std::uintmax_t& end, std::uintmax_t& total) {
    if (value.rfind(L"bytes ", 0) != 0) return false;
    const auto dash = value.find(L'-', 6);
    const auto slash = value.find(L'/', dash == std::wstring::npos ? 0 : dash + 1);
    if (dash == std::wstring::npos || slash == std::wstring::npos || dash <= 6 || slash <= dash + 1 || slash + 1 >= value.size()) {
        return false;
    }
    try {
        size_t consumedStart = 0;
        size_t consumedEnd = 0;
        size_t consumedTotal = 0;
        start = std::stoull(value.substr(6, dash - 6), &consumedStart);
        end = std::stoull(value.substr(dash + 1, slash - dash - 1), &consumedEnd);
        total = std::stoull(value.substr(slash + 1), &consumedTotal);
        return consumedStart == dash - 6 && consumedEnd == slash - dash - 1 && consumedTotal == value.size() - slash - 1 &&
               start <= end && end < total;
    } catch (...) {
        return false;
    }
}

bool QueryHttpResponse(HINTERNET request, HttpResponse& response) {
    DWORD statusSize = sizeof(response.status);
    if (!WinHttpQueryHeaders(request, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                              WINHTTP_HEADER_NAME_BY_INDEX, &response.status, &statusSize, WINHTTP_NO_HEADER_INDEX)) return false;
    DWORD contentSize = sizeof(DWORD);
    DWORD contentLength = 0;
    if (WinHttpQueryHeaders(request, WINHTTP_QUERY_CONTENT_LENGTH | WINHTTP_QUERY_FLAG_NUMBER,
                              WINHTTP_HEADER_NAME_BY_INDEX, &contentLength, &contentSize, WINHTTP_NO_HEADER_INDEX)) {
        response.contentLength = contentLength;
        response.hasContentLength = true;
    }
    if (response.status >= 300 && response.status < 400) {
        QueryHeaderText(request, WINHTTP_QUERY_LOCATION, response.location);
    }
    if (response.status == 206) {
        std::wstring contentRange;
        if (QueryHeaderText(request, WINHTTP_QUERY_CONTENT_RANGE, contentRange)) {
            response.hasContentRange = ParseContentRange(contentRange, response.rangeStart, response.rangeEnd, response.rangeTotal);
        }
    }
    return true;
}

bool DisableHttpRedirects(HINTERNET request) {
    DWORD policy = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
    return WinHttpSetOption(request, WINHTTP_OPTION_REDIRECT_POLICY, &policy, sizeof(policy)) != FALSE;
}

bool HttpGetTextOnce(const std::wstring& url, std::string& body, HttpResponse& response, std::wstring& failure) {
    URL_COMPONENTS components{};
    std::vector<wchar_t> host;
    std::vector<wchar_t> path;
    std::vector<wchar_t> extra;
    if (!CrackHttpUrl(url, components, host, path, extra, failure)) return false;
    const auto session = WinHttpOpen(L"FACM/4.0 Bootstrapper", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                                     WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!session) { failure = L"网络会话初始化失败。"; return false; }
    WinHttpSetTimeouts(session, 10000, 10000, 10000, 30000);
    const auto connection = WinHttpConnect(session, host.data(), components.nPort, 0);
    if (!connection) { WinHttpCloseHandle(session); failure = L"无法连接清单服务器。"; return false; }
    std::wstring target(path.data(), components.dwUrlPathLength);
    target.append(extra.data(), components.dwExtraInfoLength);
    const auto request = WinHttpOpenRequest(connection, L"GET", target.c_str(), nullptr, WINHTTP_NO_REFERER,
                                            WINHTTP_DEFAULT_ACCEPT_TYPES,
                                            components.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0);
    if (!request) {
        WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        failure = L"无法创建清单请求。"; return false;
    }
    if (!DisableHttpRedirects(request)) {
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        failure = L"无法启用 HTTPS 无重定向策略。"; return false;
    }
    const auto sent = WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) != FALSE &&
                      WinHttpReceiveResponse(request, nullptr) != FALSE;
    if (!sent || !QueryHttpResponse(request, response)) {
        failure = L"清单请求失败（HTTP " + std::to_wstring(response.status) + L"）。";
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return false;
    }
    if (response.status >= 300 && response.status < 400) {
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return true;
    }
    if (response.status != 200) {
        failure = L"清单请求失败（HTTP " + std::to_wstring(response.status) + L"）。";
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return false;
    }
    body.clear();
    while (true) {
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request, &available)) { failure = L"清单读取失败。"; break; }
        if (available == 0) break;
        if (body.size() + available > 8ull * 1024ull * 1024ull) { failure = L"清单超过大小限制。"; break; }
        std::vector<char> buffer(available);
        DWORD read = 0;
        if (!WinHttpReadData(request, buffer.data(), available, &read)) { failure = L"清单读取失败。"; break; }
        body.append(buffer.data(), read);
    }
    WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
    return failure.empty();
}

bool HttpGetText(const std::wstring& url, std::string& body, std::wstring& failure, int redirectDepth = 0) {
    HttpResponse response{};
    if (!HttpGetTextOnce(url, body, response, failure)) return false;
    if (response.status >= 300 && response.status < 400) {
        if (redirectDepth >= 2 || response.location.empty() || !IsApprovedGithubRedirect(response.location)) {
            failure = L"拒绝不安全或未授权的 HTTPS 重定向。";
            return false;
        }
        return HttpGetText(response.location, body, failure, redirectDepth + 1);
    }
    return response.status == 200;
}

bool VerifyDetachedSignatureWithTransports(const std::string& exactBytes, const std::wstring& sourceUrl,
                                           const std::wstring& keyId, std::wstring& failure,
                                           std::wstring* selectedTransportId = nullptr) {
    failure.clear();
    for (const auto& candidate : BuildTransportCandidates(sourceUrl)) {
        std::string signature;
        std::wstring fetchFailure;
        if (!HttpGetText(candidate.url, signature, fetchFailure)) {
            failure = fetchFailure;
            continue;
        }
        std::wstring verifyFailure;
        if (facm::bootstrapper::VerifyProductionSignature(exactBytes, keyId, signature, verifyFailure)) {
            if (selectedTransportId) *selectedTransportId = candidate.id;
            return true;
        }
        failure = verifyFailure;
    }
    return false;
}

int ProbeGithubTransport(const fs::path& root, const std::wstring& sourceUrl, const std::wstring& correlation) {
    if (!IsCanonicalGithubReleaseUrl(sourceUrl)) {
        AppendLog(root, L"free-dist-transport-probe-rejected", correlation);
        return 16;
    }
    bool anySuccess = false;
    for (const auto& candidate : BuildTransportCandidates(sourceUrl)) {
        std::string body;
        std::wstring failure;
        const auto success = HttpGetText(candidate.url, body, failure);
        AppendLogDetail(root, success ? L"free-dist-transport-probe-pass" : L"free-dist-transport-probe-fail",
                        correlation, candidate.id);
        anySuccess = anySuccess || success;
    }
    return anySuccess ? 0 : 16;
}

bool DownloadUrl(const std::wstring& url, const fs::path& partial, const ComponentManifest& component,
                 StatusWindow& status, const fs::path& root, const std::wstring& correlation,
                 std::uintmax_t overallCompleted, std::uintmax_t overallTotal,
                 std::uintmax_t& downloadedBytes, std::uintmax_t& totalBytes, std::wstring& failure,
                 int redirectDepth = 0) {
    URL_COMPONENTS components{};
    std::vector<wchar_t> host;
    std::vector<wchar_t> path;
    std::vector<wchar_t> extra;
    if (!CrackHttpUrl(url, components, host, path, extra, failure)) return false;
    std::error_code error;
    const auto existing = fs::is_regular_file(partial, error) ? fs::file_size(partial, error) : 0;
    if (error || existing > component.packageSize) {
        fs::remove(partial, error);
    }
    const auto resumeAt = fs::is_regular_file(partial, error) ? fs::file_size(partial, error) : 0;
    const auto session = WinHttpOpen(L"FACM/4.0 Bootstrapper", WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                                     WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!session) { failure = L"网络会话初始化失败。"; return false; }
    WinHttpSetTimeouts(session, 10000, 10000, 10000, 30000);
    const auto connection = WinHttpConnect(session, host.data(), components.nPort, 0);
    if (!connection) { WinHttpCloseHandle(session); failure = L"组件服务器连接失败。"; return false; }
    std::wstring target(path.data(), components.dwUrlPathLength);
    target.append(extra.data(), components.dwExtraInfoLength);
    const auto request = WinHttpOpenRequest(connection, L"GET", target.c_str(), nullptr, WINHTTP_NO_REFERER,
                                            WINHTTP_DEFAULT_ACCEPT_TYPES,
                                            components.nScheme == INTERNET_SCHEME_HTTPS ? WINHTTP_FLAG_SECURE : 0);
    if (!request) {
        WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        failure = L"无法创建组件请求。"; return false;
    }
    if (!DisableHttpRedirects(request)) {
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        failure = L"无法启用 HTTPS 无重定向策略。"; return false;
    }
    bool requestedResume = resumeAt > 0;
    std::wstring rangeHeader;
    if (requestedResume) {
        rangeHeader = L"Range: bytes=" + std::to_wstring(resumeAt) + L"-\r\n";
        WinHttpAddRequestHeaders(request, rangeHeader.c_str(), static_cast<DWORD>(-1L), WINHTTP_ADDREQ_FLAG_ADD);
    }
    const auto sent = WinHttpSendRequest(request, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0) != FALSE &&
                      WinHttpReceiveResponse(request, nullptr) != FALSE;
    HttpResponse response{};
    if (!sent || !QueryHttpResponse(request, response)) {
        failure = L"组件请求失败（HTTP " + std::to_wstring(response.status) + L"）。";
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return false;
    }
    if (response.status >= 300 && response.status < 400) {
        if (redirectDepth >= 2 || response.location.empty() || !IsApprovedGithubRedirect(response.location)) {
            failure = L"拒绝不安全或未授权的 HTTPS 重定向。";
            WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
            return false;
        }
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return DownloadUrl(response.location, partial, component, status, root, correlation, overallCompleted, overallTotal,
                           downloadedBytes, totalBytes, failure, redirectDepth + 1);
    }
    if (response.status != 200 && response.status != 206) {
        failure = L"组件请求失败（HTTP " + std::to_wstring(response.status) + L"）。";
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return false;
    }
    if (response.status == 206 && (!response.hasContentRange || response.rangeStart != resumeAt ||
                                   response.rangeTotal != component.packageSize || response.rangeEnd < response.rangeStart)) {
        failure = L"组件服务器返回了不匹配的 Range 响应，拒绝拼接。";
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return false;
    }
    if (requestedResume && response.status == 200) AppendLog(root, L"component-download-range-ignored", correlation);
    const auto append = requestedResume && response.status == 206;
    downloadedBytes = append ? resumeAt : 0;
    totalBytes = response.hasContentLength ? downloadedBytes + response.contentLength : component.packageSize;
    if (totalBytes != component.packageSize) totalBytes = component.packageSize;
    std::ofstream output(partial, append ? (std::ios::binary | std::ios::app) : (std::ios::binary | std::ios::trunc));
    if (!output) {
        failure = L"无法写入组件临时包。";
        WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
        return false;
    }
    while (true) {
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request, &available)) { failure = L"组件下载读取失败。"; break; }
        if (available == 0) break;
        std::vector<char> buffer(std::min<DWORD>(available, 256 * 1024));
        DWORD read = 0;
        if (!WinHttpReadData(request, buffer.data(), static_cast<DWORD>(buffer.size()), &read)) { failure = L"组件下载读取失败。"; break; }
        if (downloadedBytes + read > component.packageSize) { failure = L"组件下载超过清单大小。"; break; }
        output.write(buffer.data(), read);
        if (!output) { failure = L"组件临时包写入失败。"; break; }
        downloadedBytes += read;
        status.SetStatus(L"正在下载 " + component.componentId + L"：" + std::to_wstring(downloadedBytes) + L" / " + std::to_wstring(totalBytes) +
                         L" 字节；总进度 " + std::to_wstring(overallCompleted + downloadedBytes) + L" / " + std::to_wstring(overallTotal) + L" 字节");
    }
    output.close();
    WinHttpCloseHandle(request); WinHttpCloseHandle(connection); WinHttpCloseHandle(session);
    if (!failure.empty() || downloadedBytes != component.packageSize) {
        if (failure.empty()) failure = L"组件下载未完成。";
        return false;
    }
    return true;
}

std::uintmax_t DirectoryFileCount(const fs::path& root) {
    std::uintmax_t count = 0;
    std::error_code error;
    for (const auto& entry : fs::recursive_directory_iterator(root, error)) {
        if (error) break;
        if (entry.is_regular_file(error)) ++count;
    }
    return count;
}

bool AddSaturating(std::uintmax_t& total, std::uintmax_t value) {
    const auto maximum = static_cast<std::uintmax_t>(-1);
    if (value > maximum - total) {
        total = maximum;
        return false;
    }
    total += value;
    return true;
}

std::optional<std::uintmax_t> AvailableDiskBytes(const fs::path& root) {
    ULARGE_INTEGER available{};
    if (!GetDiskFreeSpaceExW(root.c_str(), &available, nullptr, nullptr)) return std::nullopt;
    return static_cast<std::uintmax_t>(available.QuadPart);
}

std::uintmax_t ProvisionRequiredBytes(const ApplicationManifest& manifest) {
    constexpr std::uintmax_t safetyMargin = 64ull * 1024ull * 1024ull;
    std::uintmax_t required = safetyMargin;
    for (const auto& component : manifest.components) {
        // Peak update usage is package/partial + extracted component staging +
        // the composed version. Existing active/known-good versions are never
        // counted as disposable space and are left untouched on failure.
        AddSaturating(required, component.packageSize);
        AddSaturating(required, component.installedSize);
        AddSaturating(required, component.installedSize);
    }
    return required;
}

bool CheckProvisionDiskSpace(const fs::path& root, const ApplicationManifest& manifest,
                             const std::wstring& correlation, std::wstring& failure) {
    const auto required = ProvisionRequiredBytes(manifest);
    const auto available = AvailableDiskBytes(root);
    if (!available) {
        failure = L"无法读取目标磁盘的可用空间，拒绝开始更新。";
        AppendLog(root, L"provision-disk-space-check-failed", correlation);
        return false;
    }
    AppendLogDetail(root, L"provision-disk-space-check", correlation,
                    L"required=" + std::to_wstring(required) + L";available=" + std::to_wstring(*available));
    if (*available < required) {
        failure = L"可用磁盘空间不足：更新峰值至少需要 " + std::to_wstring(required) +
                  L" 字节，当前可用 " + std::to_wstring(*available) + L" 字节；当前 active 未修改。";
        AppendLog(root, L"provision-disk-space-rejected", correlation);
        return false;
    }
    return true;
}

bool MergeComponentTree(const fs::path& source, const fs::path& destination, StatusWindow& status, std::wstring& failure) {
    try {
        std::error_code error;
        fs::create_directories(destination, error);
        if (error || IsReparsePoint(destination)) { failure = L"组件组合目标目录无效。"; return false; }
        for (const auto& entry : fs::recursive_directory_iterator(source, error)) {
            if (error) { failure = L"读取组件目录失败。"; return false; }
            if (entry.is_symlink(error) || IsReparsePoint(entry.path())) {
                failure = L"组件包含不允许的符号链接或重解析点。"; return false;
            }
            const auto relative = fs::relative(entry.path(), source, error);
            if (error || relative.empty()) { failure = L"组件相对路径无效。"; return false; }
            const auto target = destination / relative;
            if (!IsPathInside(destination, target)) { failure = L"组件路径越界。"; return false; }
            if (entry.is_directory(error)) {
                fs::create_directories(target, error);
                if (error || IsReparsePoint(target)) { failure = L"无法创建组合目录。"; return false; }
                continue;
            }
            if (!entry.is_regular_file(error)) { failure = L"组件目录包含不支持的文件类型。"; return false; }
            if (fs::exists(target, error) || error) { failure = L"组件文件所有权冲突：" + relative.wstring(); return false; }
            fs::create_directories(target.parent_path(), error);
            if (error || IsReparsePoint(target.parent_path())) { failure = L"无法创建组件文件目录。"; return false; }
            status.SetStatus(L"正在组合组件：" + relative.wstring());
            fs::copy_file(entry.path(), target, fs::copy_options::none, error);
            if (error) { failure = L"组件组合复制失败：" + relative.wstring(); return false; }
        }
        return true;
    } catch (...) {
        failure = L"组件组合发生未知文件系统错误。";
        return false;
    }
}

std::string ComponentManifestJson(const ComponentManifest& component) {
    std::ostringstream output;
    output << "{\n"
           << "  \"schemaVersion\": " << (component.keyId.empty() ? 2 : 3) << ",\n"
           << "  \"componentId\": \"" << EscapeJson(component.componentId) << "\",\n"
           << "  \"version\": \"" << EscapeJson(component.version) << "\",\n"
           << "  \"architecture\": \"" << EscapeJson(component.architecture) << "\",\n";
    if (!component.keyId.empty()) {
        output << "  \"keyId\": \"" << EscapeJson(component.keyId) << "\",\n";
    }
    output << "  \"required\": " << (component.required ? "true" : "false") << ",\n"
           << "  \"packageSize\": " << component.packageSize << ",\n"
           << "  \"installedSize\": " << component.installedSize << ",\n"
           << "  \"sha256\": \"" << EscapeJson(component.sha256) << "\",\n"
           << "  \"contentDigest\": \"" << EscapeJson(component.contentDigest) << "\",\n"
           << "  \"fileCount\": " << component.fileCount << ",\n"
           << "  \"packageFormat\": \"" << EscapeJson(component.packageFormat) << "\",\n"
           << "  \"entryPoint\": \"" << EscapeJson(component.entryPoint) << "\",\n"
           << "  \"primaryUrl\": \"" << EscapeJson(component.primaryUrl) << "\",\n"
           << "  \"mirrors\": [";
    for (size_t index = 0; index < component.mirrorUrls.size(); ++index) {
        if (index != 0) output << ", ";
        output << "\"" << EscapeJson(component.mirrorUrls[index]) << "\"";
    }
    output << "],\n  \"dependencies\": [";
    for (size_t index = 0; index < component.dependencies.size(); ++index) {
        if (index != 0) output << ", ";
        output << "\"" << EscapeJson(component.dependencies[index]) << "\"";
    }
    output << "],\n  \"componentManifestMirrors\": [";
    for (size_t index = 0; index < component.componentManifestMirrors.size(); ++index) {
        if (index != 0) output << ", ";
        output << "\"" << EscapeJson(component.componentManifestMirrors[index]) << "\"";
    }
    output << "]\n}\n";
    return output.str();
}

bool ValidateApplicationManifest(const ApplicationManifest& manifest, bool allowUnsignedLocal, bool allowInsecureLocal, std::wstring& failure) {
    const bool unsignedLocal = manifest.trustMode == L"unsigned-local";
    const bool production = manifest.trustMode == L"production";
    if ((!unsignedLocal && !production) || (unsignedLocal && manifest.schemaVersion != 2) ||
        (production && manifest.schemaVersion != 3) || manifest.applicationId != L"FACM" ||
        !ValidVersion(manifest.applicationVersion) ||
        manifest.architecture != kExpectedArchitecture) {
        failure = L"应用清单的 schema、应用标识、版本或架构无效。";
        return false;
    }
    if (unsignedLocal && (!allowUnsignedLocal || !allowInsecureLocal)) {
        failure = L"unsigned-local 仅允许在显式本机开发信任边界中使用。";
        return false;
    }
    if (production && manifest.keyId.empty()) {
        failure = L"生产清单缺少受信任的 key ID。";
        return false;
    }
    const auto manifestUrlAllowed = [&](const std::wstring& url) {
        return production ? IsHttpsUrl(url) : IsAllowedUrl(url, allowInsecureLocal);
    };
    std::set<std::wstring> manifestUrls;
    for (const auto& mirror : manifest.manifestMirrors) {
        if (!manifestUrlAllowed(mirror) || !manifestUrls.insert(mirror).second) {
            failure = L"应用清单镜像地址不被允许或重复。";
            return false;
        }
    }
    std::set<std::wstring> ids;
    bool hasApp = false;
    for (const auto& component : manifest.components) {
        const auto urlAllowed = [&](const std::wstring& url) {
            return production ? IsHttpsUrl(url) : IsAllowedUrl(url, allowInsecureLocal);
        };
        if (!ValidComponentId(component.componentId) || !ValidVersion(component.version) ||
            component.architecture != kExpectedArchitecture || component.packageFormat != L"cab" ||
            component.packageSize == 0 || component.installedSize == 0 || component.installedSize > kMaximumComponentInstalledBytes ||
            !IsHexSha256(component.sha256) || !IsHexSha256(component.contentDigest) || component.fileCount == 0 ||
            component.primaryUrl.empty() || !urlAllowed(component.primaryUrl)) {
            failure = L"组件清单字段无效或组件地址不被允许。";
            return false;
        }
        if (!ids.insert(component.componentId).second) {
            failure = L"组件清单包含重复组件 ID。";
            return false;
        }
        fs::path entry;
        if (!component.entryPoint.empty() && !IsSafeArchivePath(WideToUtf8(component.entryPoint), entry)) {
            failure = L"组件入口路径不安全。";
            return false;
        }
        if (component.componentId == L"facm-app-win-x64") {
            hasApp = component.entryPoint == kCoreEntryPoint;
        }
        for (const auto& mirror : component.mirrorUrls) {
            if (!urlAllowed(mirror)) {
                failure = L"组件镜像地址不被允许。";
                return false;
            }
        }
        for (const auto& dependency : component.dependencies) {
            if (dependency == component.componentId || dependency.empty()) {
                failure = L"组件依赖关系无效。";
                return false;
            }
        }
        if (production && (component.schemaVersion != 3 || component.componentManifestUrl.empty() ||
                           !IsHttpsUrl(component.componentManifestUrl) ||
                           !IsHexSha256(component.componentManifestSha256))) {
            failure = L"生产组件缺少 HTTPS 组件清单或其精确字节摘要。";
            return false;
        }
        if (production) {
            std::set<std::wstring> componentManifestUrls;
            for (const auto& mirror : component.componentManifestMirrors) {
                if (!IsHttpsUrl(mirror) || !componentManifestUrls.insert(mirror).second) {
                    failure = L"生产组件镜像清单地址不被允许或重复。";
                    return false;
                }
            }
        }
    }
    if (!hasApp) {
        failure = L"应用清单缺少 FACM.App 组件入口。";
        return false;
    }
    for (const auto& component : manifest.components) {
        for (const auto& dependency : component.dependencies) {
            if (ids.find(dependency) == ids.end()) {
                failure = L"组件依赖缺少对应组件：" + dependency;
                return false;
            }
        }
    }
    return true;
}

const InstalledComponent* FindInstalledComponent(const ComponentsState& state, const std::wstring& id) {
    const auto found = std::find_if(state.components.begin(), state.components.end(), [&](const auto& component) {
        return component.componentId == id;
    });
    return found == state.components.end() ? nullptr : &*found;
}

bool VerifyPackAgainstManifest(const fs::path& pack, const ComponentManifest& manifest, std::wstring& failure);

bool ExistingComponentMatches(const fs::path& root, const ComponentManifest& expected, const ComponentsState& state, fs::path& path) {
    const auto installed = FindInstalledComponent(state, expected.componentId);
    if (!installed || installed->version != expected.version || installed->contentDigest.empty()) return false;
    const auto candidate = root / fs::path(installed->path);
    if (!IsPathInside(root / L".facm" / L"components", candidate) || !fs::is_directory(candidate)) return false;
    std::error_code error;
    if (DirectorySize(candidate) != expected.installedSize || DirectoryFileCount(candidate) != expected.fileCount || error) return false;
    const auto digest = DirectoryDigest(candidate);
    if (digest.empty() || _wcsicmp(digest.c_str(), expected.contentDigest.c_str()) != 0) return false;
    path = candidate;
    return true;
}

bool InstallNetworkComponent(const fs::path& root, const ComponentManifest& component, bool allowInsecureLocal,
                             StatusWindow& status, const std::wstring& correlation, std::uintmax_t& overallCompleted,
                             std::uintmax_t overallTotal, fs::path& installedPath, std::wstring& failure) {
    const auto extension = ComponentPackageExtension(component);
    if (extension != L".cab") { failure = L"BOOT-2 只接受 CAB 组件包。"; return false; }
    const auto cacheDirectory = root / L".facm" / L"cache" / L"downloads";
    const auto stagingDirectory = root / L".facm" / L"staging" / (component.componentId + L"-" + component.version);
    const auto destination = root / L".facm" / L"components" / component.componentId / component.version;
    if (!IsPathInside(root / L".facm" / L"cache", cacheDirectory) ||
        !IsPathInside(root / L".facm" / L"staging", stagingDirectory) ||
        !IsPathInside(root / L".facm" / L"components", destination)) {
        failure = L"组件缓存、暂存或安装路径越界。";
        return false;
    }
    std::error_code error;
    fs::create_directories(cacheDirectory, error);
    if (error) { failure = L"无法创建组件下载缓存目录。"; return false; }
    const auto complete = cacheDirectory / (component.componentId + L"-" + component.version + extension);
    const auto partial = complete.wstring() + L".partial";
    bool verified = false;
    if (fs::is_regular_file(complete, error)) {
        std::wstring cachedFailure;
        verified = VerifyPackAgainstManifest(complete, component, cachedFailure);
        if (!verified) fs::remove(complete, error);
    }
    std::vector<std::wstring> sourceUrls;
    sourceUrls.push_back(component.primaryUrl);
    sourceUrls.insert(sourceUrls.end(), component.mirrorUrls.begin(), component.mirrorUrls.end());
    const auto urls = BuildOrderedTransportCandidates(sourceUrls);
    if (!verified) {
        for (size_t index = 0; index < urls.size(); ++index) {
            if (!IsAllowedUrl(urls[index].url, allowInsecureLocal)) continue;
            const auto partialSize = fs::is_regular_file(partial, error) ? fs::file_size(partial, error) : 0;
            if (partialSize > 0) AppendLog(root, L"component-download-resume", correlation);
            AppendLog(root, index == 0 ? L"component-download-start" : L"component-download-failover", correlation);
            AppendLogDetail(root, L"component-transport-attempt", correlation, urls[index].id);
            std::wstring downloadFailure;
            std::uintmax_t downloaded = 0;
            std::uintmax_t total = 0;
            if (!DownloadUrl(urls[index].url, partial, component, status, root, correlation, overallCompleted, overallTotal,
                             downloaded, total, downloadFailure)) {
                AppendLogDetail(root, L"component-transport-failure", correlation, urls[index].id);
                AppendLog(root, L"component-download-failed", correlation);
                continue;
            }
            std::wstring verifyFailure;
            if (!VerifyPackAgainstManifest(partial, component, verifyFailure)) {
                AppendLogDetail(root, L"component-transport-verification-failed", correlation, urls[index].id);
                AppendLog(root, L"component-download-hash-failed", correlation);
                fs::remove(partial, error);
                continue;
            }
            if (!MoveFileExW(partial.c_str(), complete.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
                failure = L"组件包校验通过但无法转正缓存。";
                return false;
            }
            AppendLog(root, L"component-download-complete", correlation);
            AppendLogDetail(root, L"component-transport-selected", correlation, urls[index].id);
            verified = true;
            break;
        }
    }
    if (!verified) { failure = L"组件下载失败，所有地址均不可用或校验未通过。"; return false; }
    overallCompleted += component.packageSize;

    if (fs::is_directory(destination, error)) {
        if (DirectorySize(destination) == component.installedSize && DirectoryFileCount(destination) == component.fileCount &&
            _wcsicmp(DirectoryDigest(destination).c_str(), component.contentDigest.c_str()) == 0) {
            installedPath = destination;
            overallCompleted += component.packageSize;
            AppendLog(root, L"component-install-reused", correlation);
            return true;
        }
        failure = L"已有组件目录与目标版本不一致，拒绝覆盖。";
        return false;
    }
    fs::remove_all(stagingDirectory, error);
    if (error) { failure = L"无法清理组件暂存目录。"; return false; }
    if (!ExtractCab(complete, stagingDirectory, component, status, failure)) {
        // Preserve failed staging for diagnostics and a future cleanup/retry pass.
        return false;
    }
    if (DirectorySize(stagingDirectory) != component.installedSize ||
        DirectoryFileCount(stagingDirectory) != component.fileCount ||
        _wcsicmp(DirectoryDigest(stagingDirectory).c_str(), component.contentDigest.c_str()) != 0) {
        failure = L"组件解包后的文件数量、大小或 contentDigest 与 authenticated metadata 不一致。";
        // Preserve failed staging for diagnostics and a future cleanup/retry pass.
        return false;
    }
    fs::create_directories(destination.parent_path(), error);
    if (error || !MoveFileExW(stagingDirectory.c_str(), destination.c_str(), MOVEFILE_WRITE_THROUGH)) {
        fs::remove_all(stagingDirectory, error);
        failure = L"组件安装激活失败，旧 active 未修改。";
        return false;
    }
    if (!AtomicWrite(destination.parent_path() / (component.version + L".manifest.json"), ComponentManifestJson(component))) {
        failure = L"组件本地清单写入失败；组件目录保留但 active 未切换。";
        return false;
    }
    AppendLog(root, L"component-extraction-complete", correlation);
    installedPath = destination;
    return true;
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
           argument == L"--probe-github-transport" || argument.rfind(L"--probe-github-transport=", 0) == 0 ||
           argument == L"--provision-source" || argument.rfind(L"--provision-source=", 0) == 0 ||
           argument == L"--provision-pack" || argument.rfind(L"--provision-pack=", 0) == 0 ||
           argument == L"--manifest" || argument.rfind(L"--manifest=", 0) == 0 ||
           argument == L"--activate-version" || argument.rfind(L"--activate-version=", 0) == 0 ||
           argument == L"--verify-pack" || argument.rfind(L"--verify-pack=", 0) == 0 ||
           argument == L"--verify-trust-bundle" || argument.rfind(L"--verify-trust-bundle=", 0) == 0 ||
           argument == L"--version" || argument.rfind(L"--version=", 0) == 0 ||
           argument == L"--update" || argument == L"--allow-insecure-local" || argument == L"--allow-unsigned-local" ||
           argument == L"--manifest-url" || argument.rfind(L"--manifest-url=", 0) == 0 ||
           argument == L"--check-disk-space" || argument.rfind(L"--check-disk-space=", 0) == 0;
}

bool TakesBootstrapValue(const std::wstring& argument) {
    return argument == L"--probe-github-transport" || argument == L"--provision-source" || argument == L"--provision-pack" ||
           argument == L"--manifest" || argument == L"--activate-version" ||
           argument == L"--verify-pack" || argument == L"--verify-trust-bundle" || argument == L"--version" ||
           argument == L"--manifest-url" || argument == L"--check-disk-space";
}

fs::path StatePath(const fs::path& root) {
    return root / L".facm" / L"state" / kStateFileName;
}

fs::path ComponentsStatePath(const fs::path& root) {
    return root / L".facm" / L"state" / kComponentsStateFileName;
}

std::optional<BootstrapConfig> ReadBootstrapConfig(const fs::path& root) {
    std::string json;
    const auto path = root / kBootstrapConfigFileName;
    if (!ReadText(path, json)) return std::nullopt;
    const auto manifestUrl = JsonString(json, "manifestUrl");
    if (!manifestUrl || manifestUrl->empty()) return std::nullopt;
    BootstrapConfig config;
    config.manifestUrl = Utf8ToWide(*manifestUrl);
    config.manifestMirrors = JsonStringArray(json, "manifestMirrors");
    config.allowUnsignedLocal = JsonBool(json, "allowUnsignedLocal").value_or(false);
    config.allowInsecureLocal = JsonBool(json, "allowInsecureLocal").value_or(false);
    return config;
}

std::optional<ComponentsState> ReadComponentsState(const fs::path& path) {
    std::string json;
    if (!ReadText(path, json)) return std::nullopt;
    ComponentsState state;
    state.schemaVersion = static_cast<int>(JsonUnsigned(json, "schemaVersion").value_or(0));
    state.applicationVersion = Utf8ToWide(JsonString(json, "applicationVersion").value_or(""));
    std::set<std::wstring> seen;
    for (const auto& object : JsonObjectArray(json, "components")) {
        const auto componentId = JsonString(object, "componentId");
        const auto version = JsonString(object, "version");
        const auto componentPath = JsonString(object, "path");
        if (!componentId || !version || !componentPath) return std::nullopt;
        InstalledComponent component;
        component.componentId = Utf8ToWide(*componentId);
        component.version = Utf8ToWide(*version);
        component.path = Utf8ToWide(*componentPath);
        component.installedSize = JsonUnsigned(object, "installedSize").value_or(0);
        component.contentDigest = Utf8ToWide(JsonString(object, "contentDigest").value_or(""));
        if (!ValidComponentId(component.componentId) || !ValidVersion(component.version) ||
            component.installedSize == 0 || !IsHexSha256(component.contentDigest) ||
            !IsSafeInstalledComponentPath(component.path)) return std::nullopt;
        if (!seen.insert(component.componentId).second) return std::nullopt;
        state.components.push_back(component);
    }
    if (state.schemaVersion != 1 || state.applicationVersion.empty() || state.components.empty()) return std::nullopt;
    return state;
}

std::string ComponentsStateJson(const ComponentsState& state) {
    std::ostringstream output;
    output << "{\n"
           << "  \"schemaVersion\": 1,\n"
           << "  \"applicationVersion\": \"" << EscapeJson(state.applicationVersion) << "\",\n"
           << "  \"components\": [\n";
    for (size_t index = 0; index < state.components.size(); ++index) {
        const auto& component = state.components[index];
        output << "    {\"componentId\": \"" << EscapeJson(component.componentId)
               << "\", \"version\": \"" << EscapeJson(component.version)
               << "\", \"path\": \"" << EscapeJson(component.path)
               << "\", \"installedSize\": " << component.installedSize
               << ", \"contentDigest\": \"" << EscapeJson(component.contentDigest) << "\"}";
        if (index + 1 != state.components.size()) output << ',';
        output << '\n';
    }
    output << "  ]\n}\n";
    return output.str();
}

bool WriteComponentsState(const fs::path& root, const ComponentsState& state) {
    return AtomicWrite(ComponentsStatePath(root), ComponentsStateJson(state));
}

bool WriteActiveState(const fs::path& root, const ActiveState& state) {
    return AtomicWrite(StatePath(root), ActiveStateJson(state));
}

bool VerifyPackAgainstManifest(const fs::path& pack, const ComponentManifest& manifest, std::wstring& failure) {
    if (!ValidComponentId(manifest.componentId) || manifest.architecture != kExpectedArchitecture || !IsHexSha256(manifest.sha256) ||
        manifest.packageSize == 0 || manifest.installedSize == 0 || manifest.installedSize > kMaximumComponentInstalledBytes) {
        failure = L"组件清单的组件 ID、架构或 SHA-256 无效。";
        return false;
    }
    std::error_code error;
    if (!fs::is_regular_file(pack, error)) {
        failure = L"组件包不存在。";
        return false;
    }
    if (fs::file_size(pack, error) != manifest.packageSize || error) {
        failure = L"组件包大小与清单不一致。";
        return false;
    }
    const auto actual = Sha256File(pack);
    if (!actual || _wcsicmp(actual->c_str(), manifest.sha256.c_str()) != 0) {
        failure = L"组件包 SHA-256 校验失败。";
        return false;
    }
    return true;
}

bool VerifyPack(const fs::path& pack, const fs::path& manifestPath, std::wstring& failure) {
    const auto manifest = ReadComponentManifest(manifestPath);
    if (!manifest) {
        failure = L"组件清单缺失或格式无效。";
        return false;
    }
    return VerifyPackAgainstManifest(pack, *manifest, failure);
}

bool SameStringVector(const std::vector<std::wstring>& left, const std::vector<std::wstring>& right) {
    return left == right;
}

bool SameComponentMetadata(const ComponentManifest& advertised, const ComponentManifest& authenticated) {
    return authenticated.schemaVersion == 3 &&
           advertised.componentId == authenticated.componentId &&
           advertised.version == authenticated.version &&
           advertised.architecture == authenticated.architecture &&
           advertised.required == authenticated.required &&
           advertised.packageSize == authenticated.packageSize &&
           advertised.installedSize == authenticated.installedSize &&
           _wcsicmp(advertised.sha256.c_str(), authenticated.sha256.c_str()) == 0 &&
           advertised.entryPoint == authenticated.entryPoint &&
           advertised.packageFormat == authenticated.packageFormat &&
           _wcsicmp(advertised.contentDigest.c_str(), authenticated.contentDigest.c_str()) == 0 &&
           advertised.fileCount == authenticated.fileCount &&
           advertised.primaryUrl == authenticated.primaryUrl &&
           SameStringVector(advertised.mirrorUrls, authenticated.mirrorUrls) &&
           SameStringVector(advertised.componentManifestMirrors, authenticated.componentManifestMirrors) &&
           SameStringVector(advertised.dependencies, authenticated.dependencies);
}

bool VerifyProductionApplicationManifest(const std::string& exactBytes, const std::wstring& manifestUrl,
                                         const ApplicationManifest& manifest, std::wstring& failure) {
    failure.clear();
    if (manifest.trustMode != L"production" || !IsHttpsUrl(manifestUrl) || manifest.keyId.empty()) {
        failure = L"生产清单必须使用 HTTPS、production trust mode 和受信任 key ID。";
        return false;
    }
    if (!VerifyDetachedSignatureWithTransports(exactBytes, DetachedSignatureUrl(manifestUrl), manifest.keyId, failure)) {
        failure = L"无法读取应用清单 detached 签名：" + failure;
        return false;
    }
    return true;
}

bool VerifyProductionComponentManifest(const ComponentManifest& advertised, const std::wstring& applicationKeyId,
                                       std::wstring& failure) {
    if (advertised.componentManifestUrl.empty() || !IsHttpsUrl(advertised.componentManifestUrl) ||
        !IsHexSha256(advertised.componentManifestSha256)) {
        failure = L"生产组件清单地址或精确字节摘要无效。";
        return false;
    }
    std::vector<std::wstring> manifestSources;
    manifestSources.push_back(advertised.componentManifestUrl);
    manifestSources.insert(manifestSources.end(), advertised.componentManifestMirrors.begin(), advertised.componentManifestMirrors.end());
    const auto manifestUrls = BuildOrderedTransportCandidates(manifestSources);
    std::string exactBytes;
    std::wstring selectedSourceUrl;
    std::wstring lastFailure;
    for (const auto& candidate : manifestUrls) {
        if (!IsHttpsUrl(candidate.url)) continue;
        std::wstring fetchFailure;
        if (HttpGetText(candidate.url, exactBytes, fetchFailure)) {
            const auto digest = Sha256Text(exactBytes);
            if (digest && _wcsicmp(digest->c_str(), advertised.componentManifestSha256.c_str()) == 0) {
                selectedSourceUrl = candidate.sourceUrl;
                break;
            }
            lastFailure = L"组件清单精确字节 SHA-256 与应用清单不一致。";
            continue;
        }
        lastFailure = fetchFailure;
    }
    if (selectedSourceUrl.empty()) {
        failure = L"无法读取或校验组件清单：" + (lastFailure.empty() ? L"没有可用的受控地址。" : lastFailure);
        return false;
    }
    const auto authenticated = ParseComponentManifest(exactBytes);
    if (!authenticated || authenticated->schemaVersion != 3 || authenticated->keyId != applicationKeyId) {
        failure = L"组件清单 key ID 或 schema 与应用清单不一致。";
        return false;
    }
    if (!VerifyDetachedSignatureWithTransports(exactBytes, DetachedSignatureUrl(selectedSourceUrl), authenticated->keyId, failure)) {
        failure = L"无法读取组件清单 detached 签名：" + failure;
        return false;
    }
    if (!SameComponentMetadata(advertised, *authenticated)) {
        failure = L"组件清单未通过应用清单的 authenticated metadata 对比。";
        return false;
    }
    return true;
}

bool VerifyTrustBundle(const fs::path& bundle, std::wstring& failure) {
    std::error_code error;
    if (!fs::is_directory(bundle, error)) {
        failure = L"BOOT3-A trust bundle 目录不存在。";
        return false;
    }
    const auto applicationPath = bundle / L"manifest.json";
    const auto applicationSignaturePath = bundle / L"manifest.json.sig";
    std::string applicationBytes;
    std::string applicationSignature;
    if (!ReadText(applicationPath, applicationBytes) || !ReadText(applicationSignaturePath, applicationSignature)) {
        failure = L"BOOT3-A trust bundle 缺少应用清单或 detached 签名。";
        return false;
    }
    const auto manifest = ReadApplicationManifest(applicationBytes);
    if (!manifest || !facm::bootstrapper::VerifyProductionSignature(applicationBytes, manifest->keyId, applicationSignature, failure)) return false;
    if (!ValidateApplicationManifest(*manifest, false, false, failure)) return false;

    const auto verificationRoot = bundle / (L".trust-verification-" + std::to_wstring(GetCurrentProcessId()));
    fs::remove_all(verificationRoot, error);
    fs::create_directories(verificationRoot, error);
    if (error) {
        failure = L"无法创建 BOOT3-A trust bundle 校验暂存目录。";
        return false;
    }
    StatusWindow status;
    for (const auto& advertised : manifest->components) {
        const auto componentDirectory = bundle / L"components" / advertised.componentId / advertised.version;
        const auto componentManifestPath = componentDirectory / L"component.manifest.json";
        const auto componentSignaturePath = componentDirectory / L"component.manifest.json.sig";
        const auto pack = componentDirectory / (advertised.componentId + L"-" + advertised.version + L".cab");
        if (!IsPathInside(bundle, componentDirectory) || !IsPathInside(bundle, componentManifestPath) ||
            !IsPathInside(bundle, componentSignaturePath) || !IsPathInside(bundle, pack)) {
            failure = L"BOOT3-A trust bundle 组件路径越界。";
            fs::remove_all(verificationRoot, error);
            return false;
        }
        std::string componentBytes;
        std::string componentSignature;
        if (!ReadText(componentManifestPath, componentBytes) || !ReadText(componentSignaturePath, componentSignature)) {
            failure = L"BOOT3-A trust bundle 缺少组件清单或 detached 签名。";
            fs::remove_all(verificationRoot, error);
            return false;
        }
        const auto componentDigest = Sha256Text(componentBytes);
        if (!componentDigest || _wcsicmp(componentDigest->c_str(), advertised.componentManifestSha256.c_str()) != 0) {
            failure = L"组件清单精确字节 SHA-256 与应用清单不一致。";
            fs::remove_all(verificationRoot, error);
            return false;
        }
        const auto authenticated = ParseComponentManifest(componentBytes);
        if (!authenticated || authenticated->keyId != manifest->keyId ||
            !facm::bootstrapper::VerifyProductionSignature(componentBytes, authenticated->keyId, componentSignature, failure) ||
            !SameComponentMetadata(advertised, *authenticated)) {
            if (failure.empty()) failure = L"组件清单签名或 authenticated metadata 对比失败。";
            fs::remove_all(verificationRoot, error);
            return false;
        }
        if (!VerifyPackAgainstManifest(pack, *authenticated, failure)) {
            fs::remove_all(verificationRoot, error);
            return false;
        }
        const auto extracted = verificationRoot / (advertised.componentId + L"-" + advertised.version);
        if (!ExtractCab(pack, extracted, *authenticated, status, failure)) {
            fs::remove_all(verificationRoot, error);
            return false;
        }
        if (DirectorySize(extracted) != authenticated->installedSize ||
            DirectoryFileCount(extracted) != authenticated->fileCount ||
            _wcsicmp(DirectoryDigest(extracted).c_str(), authenticated->contentDigest.c_str()) != 0) {
            failure = L"组件解包后的文件数量、大小或 contentDigest 校验失败。";
            fs::remove_all(verificationRoot, error);
            return false;
        }
    }
    fs::remove_all(verificationRoot, error);
    return true;
}

bool ProvisionFromNetwork(const fs::path& root, const std::vector<std::wstring>& manifestUrls, bool allowUnsignedLocal,
                          bool allowInsecureLocal, StatusWindow& status, const std::wstring& correlation,
                          std::wstring& failure) {
    if (manifestUrls.empty()) {
        failure = L"没有配置应用清单地址。";
        return false;
    }
    for (const auto& url : manifestUrls) {
        if (!IsAllowedUrl(url, allowInsecureLocal)) {
            failure = L"应用清单地址不是 HTTPS，且未启用显式本地开发 HTTP。";
            return false;
        }
    }
    const auto transports = BuildOrderedTransportCandidates(manifestUrls);
    if (transports.empty()) {
        failure = L"没有可用的应用清单传输候选。";
        return false;
    }
    AppendLog(root, L"manifest-fetch-start", correlation);
    std::string manifestText;
    std::wstring selectedManifestSourceUrl;
    std::wstring selectedManifestTransportId;
    for (size_t index = 0; index < transports.size(); ++index) {
        const auto& transport = transports[index];
        AppendLogDetail(root, L"manifest-transport-attempt", correlation, transport.id);
        std::wstring fetchFailure;
        if (HttpGetText(transport.url, manifestText, fetchFailure)) {
            selectedManifestSourceUrl = transport.sourceUrl;
            selectedManifestTransportId = transport.id;
            if (index != 0) AppendLog(root, L"manifest-fetch-failover", correlation);
            break;
        }
        failure = fetchFailure;
        AppendLogDetail(root, L"manifest-transport-failed", correlation, transport.id);
        if (index + 1 < transports.size()) AppendLog(root, L"manifest-fetch-failover", correlation);
    }
    if (selectedManifestSourceUrl.empty()) {
        AppendLog(root, L"manifest-fetch-failed", correlation);
        return false;
    }
    const auto resolvedManifestUrl = selectedManifestSourceUrl;
    AppendLogDetail(root, L"manifest-fetch-selected", correlation, selectedManifestTransportId);
    auto manifest = ReadApplicationManifest(manifestText);
    if (!manifest) {
        failure = L"应用清单缺失或格式无效。";
        AppendLog(root, L"manifest-validate-failed", correlation);
        return false;
    }
    if (manifest->trustMode == L"unsigned-local") {
        if (!allowUnsignedLocal || !allowInsecureLocal || !IsLocalDevelopmentUrl(resolvedManifestUrl)) {
            failure = L"unsigned-local 清单只能通过显式本机开发信任边界使用。";
            AppendLog(root, L"manifest-trust-rejected", correlation);
            return false;
        }
    } else if (manifest->trustMode == L"production") {
        if (!VerifyProductionApplicationManifest(manifestText, resolvedManifestUrl, *manifest, failure)) {
            AppendLog(root, L"manifest-signature-rejected", correlation);
            return false;
        }
        for (auto& component : manifest->components) {
            if (!VerifyProductionComponentManifest(component, manifest->keyId, failure)) {
                AppendLog(root, L"component-manifest-signature-rejected", correlation);
                return false;
            }
            component.keyId = manifest->keyId;
        }
    } else {
        failure = L"应用清单 trust mode 未知，拒绝继续。";
        AppendLog(root, L"manifest-trust-rejected", correlation);
        return false;
    }
    if (!ValidateApplicationManifest(*manifest, allowUnsignedLocal, allowInsecureLocal, failure)) {
        AppendLog(root, L"manifest-validate-failed", correlation);
        return false;
    }
    const auto currentActive = ReadActiveState(StatePath(root));
    if (manifest->trustMode == L"production" && currentActive &&
        CompareReleaseVersions(manifest->applicationVersion, currentActive->activeVersion) < 0) {
        failure = L"生产更新版本低于当前 active 版本，拒绝 downgrade。";
        AppendLog(root, L"manifest-downgrade-rejected", correlation);
        return false;
    }
    AppendLog(root, L"manifest-validated", correlation);

    const auto previousState = ReadComponentsState(ComponentsStatePath(root));
    const auto previousActive = ReadActiveState(StatePath(root));
    std::error_code currentError;
    bool allCurrent = previousState && previousState->applicationVersion == manifest->applicationVersion && previousActive &&
                      previousActive->activeVersion == manifest->applicationVersion &&
                      fs::is_regular_file(root / fs::path(previousActive->activePath) / kCoreEntryPoint, currentError);
    if (allCurrent) {
        for (const auto& component : manifest->components) {
            fs::path existing;
            if (!ExistingComponentMatches(root, component, *previousState, existing)) {
                allCurrent = false;
                break;
            }
        }
    }
    AppendLog(root, allCurrent ? L"component-evaluation-no-change" : L"component-evaluation-update-required", correlation);
    if (allCurrent) return true;
    if (!CheckProvisionDiskSpace(root, *manifest, correlation, failure)) return false;

    ComponentsState nextState;
    nextState.applicationVersion = manifest->applicationVersion;
    std::map<std::wstring, fs::path> componentPaths;
    std::set<std::wstring> completed;
    std::uintmax_t overallCompleted = 0;
    std::uintmax_t overallTotal = 0;
    for (const auto& component : manifest->components) overallTotal += component.packageSize;
    size_t remaining = manifest->components.size();
    while (remaining > 0) {
        bool progressed = false;
        for (const auto& component : manifest->components) {
            if (completed.find(component.componentId) != completed.end()) continue;
            bool dependenciesReady = true;
            for (const auto& dependency : component.dependencies) {
                if (completed.find(dependency) == completed.end()) dependenciesReady = false;
            }
            if (!dependenciesReady) continue;
            progressed = true;
            fs::path installed;
            if (previousState && ExistingComponentMatches(root, component, *previousState, installed)) {
                overallCompleted += component.packageSize;
                AppendLog(root, L"component-reused", correlation);
            } else if (!InstallNetworkComponent(root, component, allowInsecureLocal, status, correlation, overallCompleted, overallTotal, installed, failure)) {
                AppendLogDetail(root, L"component-install-failed", correlation, failure);
                return false;
            }
            if (!IsPathInside(root / L".facm" / L"components", installed)) {
                failure = L"组件安装路径不在受控目录内。";
                return false;
            }
            InstalledComponent installedState;
            installedState.componentId = component.componentId;
            installedState.version = component.version;
            std::error_code relativeError;
            installedState.path = fs::relative(installed, root, relativeError).generic_wstring();
            if (relativeError) {
                failure = L"组件安装路径无法转换为相对路径。";
                return false;
            }
            installedState.installedSize = component.installedSize;
            installedState.contentDigest = component.contentDigest;
            nextState.components.push_back(installedState);
            componentPaths.emplace(component.componentId, installed);
            completed.insert(component.componentId);
            --remaining;
        }
        if (!progressed) {
            failure = L"组件依赖关系无法解析。";
            return false;
        }
    }

    const auto compositionStaging = root / L".facm" / L"staging" / (L"composition-" + manifest->applicationVersion);
    const auto compositionDestination = root / L".facm" / L"versions" / manifest->applicationVersion;
    std::error_code error;
    if (!IsPathInside(root / L".facm" / L"staging", compositionStaging) ||
        !IsPathInside(root / L".facm" / L"versions", compositionDestination)) {
        failure = L"组合暂存或版本路径越界。";
        return false;
    }
    if (fs::exists(compositionDestination, error)) {
        failure = L"目标应用组合版本已经存在，拒绝覆盖。";
        return false;
    }
    fs::remove_all(compositionStaging, error);
    if (error) { failure = L"无法清理应用组合暂存目录。"; return false; }
    fs::create_directories(compositionStaging, error);
    if (error) { failure = L"无法创建应用组合暂存目录。"; return false; }
    for (const auto& component : manifest->components) {
        const auto found = componentPaths.find(component.componentId);
        if (found == componentPaths.end() || !MergeComponentTree(found->second, compositionStaging, status, failure)) {
            fs::remove_all(compositionStaging, error);
            return false;
        }
    }
    const auto executable = compositionStaging / kCoreEntryPoint;
    if (!fs::is_regular_file(executable, error) || fs::file_size(executable, error) == 0) {
        fs::remove_all(compositionStaging, error);
        failure = L"应用组合缺少 FACM.App 入口；当前 active 未修改。";
        return false;
    }
    std::ostringstream compositionJson;
    compositionJson << "{\n  \"schemaVersion\": 1,\n  \"applicationVersion\": \""
                    << EscapeJson(manifest->applicationVersion) << "\",\n  \"components\": [";
    for (size_t index = 0; index < manifest->components.size(); ++index) {
        if (index != 0) compositionJson << ", ";
        compositionJson << "{\"componentId\": \"" << EscapeJson(manifest->components[index].componentId)
                        << "\", \"version\": \"" << EscapeJson(manifest->components[index].version) << "\"}";
    }
    compositionJson << "]\n}\n";
    if (!AtomicWrite(compositionStaging / L"composition.json", compositionJson.str())) {
        fs::remove_all(compositionStaging, error);
        failure = L"应用组合清单写入失败。";
        return false;
    }
    fs::create_directories(compositionDestination.parent_path(), error);
    if (error || !MoveFileExW(compositionStaging.c_str(), compositionDestination.c_str(), MOVEFILE_WRITE_THROUGH)) {
        fs::remove_all(compositionStaging, error);
        failure = L"应用组合激活失败；当前 active 未修改。";
        return false;
    }
    AppendLog(root, L"composition-activated", correlation);
    if (!WriteComponentsState(root, nextState)) {
        failure = L"组件状态写入失败；当前 active 未切换。";
        return false;
    }
    ActiveState nextActive;
    nextActive.activeVersion = manifest->applicationVersion;
    nextActive.activePath = (fs::path(L".facm") / L"versions" / manifest->applicationVersion).generic_wstring();
    nextActive.previousVersion = previousActive ? previousActive->activeVersion : L"";
    if (!WriteActiveState(root, nextActive)) {
        failure = L"active.json 原子写入失败；已安装组件与组合目录保留，旧 active 未删除。";
        return false;
    }
    AppendLog(root, L"active-composition-committed", correlation);
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

    const auto transportProbe = OptionValue(arguments, L"--probe-github-transport");
    if (transportProbe) {
        const auto result = ProbeGithubTransport(root, *transportProbe, correlation);
        if (mutex) CloseHandle(mutex);
        return result;
    }

    const auto diskSpaceCheck = OptionValue(arguments, L"--check-disk-space");
    if (diskSpaceCheck) {
        std::uintmax_t required = 0;
        bool parsed = false;
        try {
            size_t consumed = 0;
            required = std::stoull(*diskSpaceCheck, &consumed);
            parsed = consumed == diskSpaceCheck->size();
        } catch (...) {
            parsed = false;
        }
        const auto available = AvailableDiskBytes(root);
        const bool sufficient = parsed && available && *available >= required;
        AppendLogDetail(root, sufficient ? L"disk-space-diagnostic-pass" : L"disk-space-diagnostic-fail", correlation,
                        L"required=" + std::to_wstring(required) + L";available=" +
                        (available ? std::to_wstring(*available) : L"unknown"));
        if (!sufficient && !g_suppressUi) ErrorMessage(L"FACM 磁盘空间检查", L"目标磁盘可用空间不足或检查参数无效。");
        if (mutex) CloseHandle(mutex);
        return sufficient ? 0 : 15;
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

    const auto trustBundle = OptionValue(arguments, L"--verify-trust-bundle");
    if (trustBundle) {
        std::wstring failure;
        const auto success = VerifyTrustBundle(*trustBundle, failure);
        if (!success && !failure.empty()) ErrorMessage(L"FACM BOOT3-A trust 校验", failure);
        if (mutex) CloseHandle(mutex);
        return success ? 0 : 21;
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

    const auto currentActive = ReadActiveState(StatePath(root));
    std::error_code activeError;
    const bool currentActiveValid = currentActive &&
        IsPathInside(root / L".facm" / L"versions", root / fs::path(currentActive->activePath)) &&
        fs::is_regular_file(root / fs::path(currentActive->activePath) / kCoreEntryPoint, activeError);
    const bool networkRequested = HasOption(arguments, L"--update") || !currentActiveValid;
    if (networkRequested) {
        const auto config = ReadBootstrapConfig(root);
        const auto configuredManifestUrl = OptionValue(arguments, L"--manifest-url").value_or(config ? config->manifestUrl : L"");
        const bool allowUnsignedLocal = HasOption(arguments, L"--allow-unsigned-local") || (config && config->allowUnsignedLocal);
        const bool allowInsecureLocal = HasOption(arguments, L"--allow-insecure-local") || (config && config->allowInsecureLocal);
        if (configuredManifestUrl.empty()) {
            if (mutex) CloseHandle(mutex);
            if (!currentActiveValid) ErrorMessage(L"FACM", L"未找到有效的 FACM 组件组合，也没有可用的 bootstrap.json。请先完成网络初始化。");
            return currentActiveValid ? 0 : 14;
        }
        std::vector<std::wstring> manifestUrls{configuredManifestUrl};
        if (!OptionValue(arguments, L"--manifest-url")) {
            if (config) manifestUrls.insert(manifestUrls.end(), config->manifestMirrors.begin(), config->manifestMirrors.end());
        }
        std::wstring failure;
        status.Show(HasOption(arguments, L"--update") ? L"正在检查 FACM 组件更新…" : L"正在初始化 FACM 组件…");
        const auto success = ProvisionFromNetwork(root, manifestUrls, allowUnsignedLocal, allowInsecureLocal, status, correlation, failure);
        status.Close();
        if (!success) {
            if (mutex) CloseHandle(mutex);
            AppendLogDetail(root, L"bootstrap-network-failed", correlation, failure);
            ErrorMessage(L"FACM 组件初始化", failure.empty() ? L"网络组件初始化失败；当前 active 版本未切换。" : failure);
            return 14;
        }
    }

    const auto result = LaunchActive(root, arguments, correlation);
    AppendLog(root, L"bootstrap-process-exit", correlation);
    if (mutex) CloseHandle(mutex);
    return result;
}
