#pragma once
#include <cstdint>
#include <cstddef>

// Decompressed AO frame, in network byte order. Only inspect its header and
// opcode; never record the payload. Rebirth sends ChatServerInfo after Claim.
namespace NexusZoneAcceptance {
    inline uint32_t Read32(const uint8_t* p) {
        return (uint32_t(p[0]) << 24) | (uint32_t(p[1]) << 16) | (uint32_t(p[2]) << 8) | p[3];
    }
    inline bool IsChatServerInfo(const void* data, size_t size, uint32_t characterId) {
        if (!data || size < 36 || size > 65535 || characterId == 0) return false;
        const auto p = static_cast<const uint8_t*>(data);
        return p[2] == 0 && p[3] == 1 && ((size_t(p[6]) << 8) | p[7]) == size
            && Read32(p + 12) == characterId && Read32(p + 16) == 0x43;
    }
}
