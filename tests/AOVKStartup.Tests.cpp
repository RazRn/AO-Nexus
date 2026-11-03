#include <windows.h>
#include <cstdlib>
#include <cstring>
#include <string>
#include <vector>
#include <map>
#include <stdexcept>
#include <cstdio>

// Exercise the real installer against inert PE images. No game or injection.
static std::vector<BYTE> guiImage(0x2B1000), rendererImage(0x1ED000), protocolImage(0xD000);
static std::map<std::string, std::string> environment;
static int attachments, rendererLookups, failAttach;
static bool aborted, wrongExport, failCommit;
static std::vector<PVOID> targets;
bool EnvironmentIs(const char* key, const char* expected) { return environment[key] == expected; }
void StatusSend(const char*, ...) {}
LONG DetourTransactionBegin() { return NO_ERROR; }
LONG DetourUpdateThread(HANDLE) { return NO_ERROR; }
LONG DetourAttach(PVOID* original, PVOID) {
    targets.push_back(*original);
    return ++attachments == failAttach ? ERROR_INVALID_DATA : NO_ERROR;
}
LONG DetourTransactionAbort() { aborted = true; return NO_ERROR; }
LONG DetourTransactionCommit() { return failCommit ? ERROR_INVALID_DATA : NO_ERROR; }
HMODULE TestModule(LPCWSTR name) {
    if (!wcscmp(name, L"GUI.dll")) return reinterpret_cast<HMODULE>(guiImage.data());
    if (!wcscmp(name, L"MessageProtocol.dll")) return reinterpret_cast<HMODULE>(protocolImage.data());
    if (!wcscmp(name, L"randy31.dll")) { ++rendererLookups; return reinterpret_cast<HMODULE>(rendererImage.data()); }
    return nullptr;
}
FARPROC TestExport(HMODULE module, LPCSTR name) {
    if (wrongExport) return nullptr;
    DWORD rva = 0;
    if (strstr(name, "DataBlockToMessage")) rva = 0x1E81;
    else if (strstr(name, "InitialiseMessage")) rva = 0x1779D;
    else if (strstr(name, "CharacterLoggedInMessage")) rva = 0x16F8F;
    else if (strstr(name, "StartClosingLoadscreenMessage")) rva = 0x16F37;
    else if (strstr(name, "FrameProcess")) rva = 0x1713C;
    else if (strstr(name, "AddMesh")) rva = 0x16000;
    return reinterpret_cast<FARPROC>(reinterpret_cast<BYTE*>(module) + rva);
}
#define GetModuleHandleW TestModule
#define GetProcAddress TestExport
#include "../src/Backend/LoginHandler/FastStartup.h"
#undef GetModuleHandleW
#undef GetProcAddress

static void Assert(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
static void Image(std::vector<BYTE>& bytes, DWORD timestamp) {
    memset(bytes.data(), 0, bytes.size());
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(bytes.data());
    dos->e_magic = IMAGE_DOS_SIGNATURE; dos->e_lfanew = 256;
    auto pe = reinterpret_cast<IMAGE_NT_HEADERS32*>(bytes.data() + 256);
    pe->Signature = IMAGE_NT_SIGNATURE; pe->FileHeader.Machine = IMAGE_FILE_MACHINE_I386;
    pe->FileHeader.TimeDateStamp = timestamp; pe->OptionalHeader.SizeOfImage = static_cast<DWORD>(bytes.size());
}
static void Reset(bool stock) {
    Image(guiImage, 0x647A09DA); Image(rendererImage, 0x647A069E); Image(protocolImage, 0x647A0672);
    *reinterpret_cast<double*>(guiImage.data() + 0x1AA198) = 7000;
    *reinterpret_cast<double*>(guiImage.data() + 0x1AA1A0) = 4000;
    const BYTE signature[] = {0x55,0x8b,0xec,0x83,0xec,0x60,0x53,0x8b,0x5d,0x10};
    memcpy(rendererImage.data() + 0x6BFC1, signature, sizeof(signature));
    environment = {{"AONexusFastStartupValidated", "True"}, {"AONexusFastTextureValidated", stock ? "True" : "False"},
        {"AONexusFastStartupProfile", stock ? "Stock" : "AOVK"}};
    attachments = rendererLookups = failAttach = 0; aborted = wrongExport = failCommit = false; targets.clear();
    NexusFastStartup::installed = NexusFastStartup::complete = 0;
    NexusFastStartup::bilinear = nullptr;
}
int main() {
    try {
        _putenv_s("AOCharId", "17");
        Reset(true);
        Assert(NexusFastStartup::Install(false, true) && attachments == 7, "Stock must retain all seven hooks");
        Assert(NexusFastStartup::bilinear != nullptr, "Stock texture hook missing");
        Reset(false);
        rendererImage[0] = 0; // Invalid/foreign renderer must never be inspected or hooked in this profile.
        Assert(NexusFastStartup::Install(false, true) && attachments == 6, "AO-VK needs the six GUI/protocol hooks");
        Assert(rendererLookups == 0 && NexusFastStartup::bilinear == nullptr, "AO-VK accessed the renderer");
        for (PVOID target : targets) Assert(target != rendererImage.data() + 0x6BFC1, "AO-VK attached a texture hook");
        Assert(NexusFastStartup::Install(false, true) && attachments == 6, "Install must be idempotent");
        Reset(false); environment["AONexusFastStartupValidated"] = "False";
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Opt-out ignored");
        Reset(false);
        Assert(!NexusFastStartup::Install(true, true) && !attachments, "Character creation must remain unmodified");
        Reset(false); environment["AONexusFastTextureValidated"] = "True";
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Contradictory profile flags accepted");
        Reset(false); environment["AONexusFastStartupProfile"] = "Unknown";
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Unknown profile accepted");
        Reset(false); guiImage[0] = 0;
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Unknown GUI accepted");
        Reset(false); protocolImage[0] = 0;
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Unknown protocol accepted");
        Reset(false); wrongExport = true;
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Wrong function address accepted");
        Reset(false); *reinterpret_cast<double*>(guiImage.data() + 0x1AA198) = 1;
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Changed fade data accepted");
        Reset(true); rendererImage[0x6BFC1] = 0;
        Assert(!NexusFastStartup::Install(false, true) && !attachments, "Stock texture signature mismatch accepted");
        Reset(false); failAttach = 3;
        Assert(!NexusFastStartup::Install(false, true) && aborted && !NexusFastStartup::Active(), "Partial attach did not abort");
        Reset(false); failCommit = true;
        Assert(!NexusFastStartup::Install(false, true) && !NexusFastStartup::Active(), "Failed commit marked active");
        puts("PASS: native stock/AO-VK hook isolation, opt-out, creation, runtime rejection, and transaction failures.");
        return 0;
    } catch (const std::exception& error) { fprintf(stderr, "%s\n", error.what()); return 1; }
}
