#pragma once
#include <cstdint>
#include <cstring>
#include "FastBilinear.h"
#include "ZoneAcceptance.h"

// First-login-only optimization for the exact 18.8.62 classic images validated
// before spawn. Never modify the game files. Keep the original decode callback.
namespace NexusFastStartup {
    using StaticFn = void (__cdecl*)();
    using AddMeshFn = void (__thiscall*)(void*, int, const char*);
    static BYTE* gui = nullptr;
    static StaticFn initialize = nullptr, loggedIn = nullptr, fadeStart = nullptr, frame = nullptr;
    static AddMeshFn addMesh = nullptr;
    static BilinearRow bilinear = nullptr;
    static bool submitted = false, worldStarted = false;
    static bool inAdjusted = false, outAdjusted = false, quiet = false;
    static volatile LONG installed = 0, complete = 0;
    static unsigned long skipped = 0;
    using DecodeFn = void* (__cdecl*)(unsigned int, void*);
    static DecodeFn decode = nullptr;
    static volatile LONG admissionReported = 0, characterRequested = 0;
    static uint32_t expectedCharacter = 0;

    static void* __cdecl DecodeHook(unsigned int size, void* data) {
        // Preserve the client's decode path, and require a successfully decoded
        // server message addressed to this launch's selected character.
        void* message = decode(size, data);
        if (message && InterlockedCompareExchange(&characterRequested, 0, 0)
            && !InterlockedCompareExchange(&admissionReported, 0, 0)
            && NexusZoneAcceptance::IsChatServerInfo(data, size, expectedCharacter)
            && InterlockedCompareExchange(&admissionReported, 1, 0) == 0)
            StatusSend("zone-accepted source=chat-server-info");
        return message;
    }

    static bool ImageMatches(BYTE* base, DWORD timestamp, DWORD size) {
        if (!base) return false;
        auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < 0 || dos->e_lfanew > 4096) return false;
        auto pe = reinterpret_cast<IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
        return pe->Signature == IMAGE_NT_SIGNATURE && pe->FileHeader.Machine == IMAGE_FILE_MACHINE_I386
            && pe->FileHeader.TimeDateStamp == timestamp && pe->OptionalHeader.SizeOfImage == size;
    }
    static bool FadeDataValid() {
        return *reinterpret_cast<const double*>(gui + 0x1AA198) == 7000.0
            && *reinterpret_cast<const double*>(gui + 0x1AA1A0) == 4000.0;
    }
    template<class T> static bool Resolve(T& target, const char* name, DWORD rva) {
        auto address = GetProcAddress(reinterpret_cast<HMODULE>(gui), name);
        if (reinterpret_cast<BYTE*>(address) != gui + rva) return false;
        target = reinterpret_cast<T>(address); return true;
    }
    static bool Complete() { return InterlockedCompareExchange(&complete, 0, 0) != 0; }
    static bool Active() { return InterlockedCompareExchange(&installed, 0, 0) != 0; }
    static void __cdecl InitializeHook() {
        initialize();
        if (!Complete() && !inAdjusted && FadeDataValid() && gui[0x274760] == 1 && gui[0x2628F0] == 1) {
            *reinterpret_cast<float*>(gui + 0x274768) -= 4000.0f;
            inAdjusted = true;
            StatusSend("fast-fade-in");
        }
    }
    static void __cdecl LoggedInHook() { worldStarted = true; loggedIn(); }
    static void __cdecl FadeStartHook() {
        fadeStart();
        if (!Complete() && worldStarted && !outAdjusted && FadeDataValid() && gui[0x274760] == 1 && gui[0x2628F0] == 0) {
            *reinterpret_cast<float*>(gui + 0x274768) -= 7000.0f;
            outAdjusted = true;
            StatusSend("fast-fade-out");
        }
    }
    static void __cdecl FrameHook() {
        bool active = gui[0x274760] != 0, direction = gui[0x2628F0] != 0;
        frame();
        if (!Complete() && worldStarted && active && !direction && gui[0x274760] == 0) {
            InterlockedExchange(&complete, 1);
            StatusSend("world-ready meshes=%lu", skipped);
        }
    }
    static void __fastcall AddMeshHook(void* self, void*, int stage, const char* name) {
        // Only the cosmetic login scene before credentials are submitted. Keep
        // its stage containers, camera, completion signal and all world objects.
        if (!Complete() && !submitted && quiet) { ++skipped; return; }
        addMesh(self, stage, name);
    }
    static uint32_t* __cdecl BilinearHook(int count, uint32_t* output, uint8_t* state) {
        if (Complete() || count < 1 || count > 8192) return bilinear(count, output, state);
        return FastBilinearRow(count, output, state);
    }
    // Called on the game thread immediately before the original SlotInitialize,
    // so texture/fade hooks are installed before initial menu construction.
    static bool Install(bool isCreation, bool isQuiet) {
        if (Active()) return true;
        if (isCreation || !EnvironmentIs("AONexusFastStartupValidated", "True")) return false;
        gui = reinterpret_cast<BYTE*>(GetModuleHandleW(L"GUI.dll"));
        auto randy = reinterpret_cast<BYTE*>(GetModuleHandleW(L"randy31.dll"));
        auto protocol = reinterpret_cast<BYTE*>(GetModuleHandleW(L"MessageProtocol.dll"));
        if (!ImageMatches(gui, 0x647A09DA, 0x2B1000) || !ImageMatches(randy, 0x647A069E, 0x1ED000)
            || !ImageMatches(protocol, 0x647A0672, 0xD000)
            || !FadeDataValid()) { StatusSend("fast-startup unsupported"); return false; }
        auto decoder = GetProcAddress(reinterpret_cast<HMODULE>(protocol), "?DataBlockToMessage@@YAPAVMessage_t@@IPAX@Z");
        if (reinterpret_cast<BYTE*>(decoder) != protocol + 0x1E81) return false;
        decode = reinterpret_cast<DecodeFn>(decoder);
        expectedCharacter = static_cast<uint32_t>(atoi(std::getenv("AOCharId")));
        const uint8_t signature[] = {0x55,0x8b,0xec,0x83,0xec,0x60,0x53,0x8b,0x5d,0x10};
        if (memcmp(randy + 0x6BFC1, signature, sizeof(signature))
            || !Resolve(initialize, "?InitialiseMessage@ServerLogin3DModule_t@@CAXXZ", 0x1779D)
            || !Resolve(loggedIn, "?CharacterLoggedInMessage@ServerLogin3DModule_t@@CAXXZ", 0x16F8F)
            || !Resolve(fadeStart, "?StartClosingLoadscreenMessage@ServerLogin3DModule_t@@CAXXZ", 0x16F37)
            || !Resolve(frame, "?FrameProcess@ServerLogin3DModule_t@@CAXXZ", 0x1713C)
            || !Resolve(addMesh, "?AddMesh@LoginWorld_c@@AAEXW4Stage_e@1@PBD@Z", 0x16000)) {
            StatusSend("fast-startup unsupported"); return false;
        }
        bilinear = reinterpret_cast<BilinearRow>(randy + 0x6BFC1);
        quiet = isQuiet;
        if (DetourTransactionBegin() != NO_ERROR) return false;
        LONG result = DetourUpdateThread(GetCurrentThread());
#define NEXUS_FAST_ATTACH(original, hook) if (result == NO_ERROR) result = DetourAttach(reinterpret_cast<PVOID*>(&(original)), reinterpret_cast<PVOID>(hook))
        NEXUS_FAST_ATTACH(initialize, InitializeHook);
        NEXUS_FAST_ATTACH(loggedIn, LoggedInHook);
        NEXUS_FAST_ATTACH(fadeStart, FadeStartHook);
        NEXUS_FAST_ATTACH(frame, FrameHook);
        NEXUS_FAST_ATTACH(addMesh, AddMeshHook);
        NEXUS_FAST_ATTACH(bilinear, BilinearHook);
        NEXUS_FAST_ATTACH(decode, DecodeHook);
#undef NEXUS_FAST_ATTACH
        if (result != NO_ERROR) { DetourTransactionAbort(); StatusSend("fast-startup attach-failed"); return false; }
        bool attached = DetourTransactionCommit() == NO_ERROR;
        InterlockedExchange(&installed, attached ? 1 : 0);
        StatusSend(attached ? "fast-startup active" : "fast-startup commit-failed");
        return attached;
    }
}
