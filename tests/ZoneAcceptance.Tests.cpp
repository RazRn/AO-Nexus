#include <windows.h>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <initializer_list>
#include "../src/Backend/LoginHandler/ZoneAcceptance.h"

static unsigned checks = 0;
static void Check(bool condition) { ++checks; if (!condition) std::abort(); }
int main() {
    using NexusZoneAcceptance::IsChatServerInfo;
    uint8_t packet[40] = {0xdf,0xdf,0,1,0,1,0,40,0,0,0,1,0,0,0,166,0,0,0,0x43};
    Check(IsChatServerInfo(packet, sizeof(packet), 166));
    Check(!IsChatServerInfo(packet, sizeof(packet), 170));
    Check(!IsChatServerInfo(packet, sizeof(packet), 0));
    Check(!IsChatServerInfo(nullptr, sizeof(packet), 166));
    Check(!IsChatServerInfo(packet, 65536, 166));
    for (size_t length = 0; length < sizeof(packet); ++length)
        Check(!IsChatServerInfo(packet, length, 166));
    for (size_t offset : {size_t(2),size_t(3),size_t(6),size_t(7),size_t(12),size_t(13),size_t(14),size_t(15),size_t(16),size_t(17),size_t(18),size_t(19)}) {
        packet[offset] ^= 1;
        Check(!IsChatServerInfo(packet, sizeof(packet), 166));
        packet[offset] ^= 1;
    }
    // A truncated frame abutting a protected page must not trigger an overread.
    auto pages = static_cast<uint8_t*>(VirtualAlloc(nullptr, 8192, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE));
    Check(pages != nullptr);
    DWORD old = 0; Check(VirtualProtect(pages + 4096, 4096, PAGE_NOACCESS, &old) != 0);
    for (size_t length = 0; length < 36; ++length) {
        auto tail = pages + 4096 - length;
        if (length) memcpy(tail, packet, length);
        Check(!IsChatServerInfo(tail, length, 166));
    }
    VirtualFree(pages, 0, MEM_RELEASE);
    printf("PASS: %u acceptance frame validation checks, including guard-page truncation.\n", checks);
}
