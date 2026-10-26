#pragma once
#include <cstdint>
#include <cstring>

// Version-specific row resampler ABI, derived from randy31.dll 18.8.62.
// Keep the client's format/color-key decoder; optimize only address/weight arithmetic.
using BilinearRow = uint32_t* (__cdecl*)(int, uint32_t*, uint8_t*);
using PixelDecoder = void (__cdecl*)(const uint8_t**, uint32_t*, uint8_t*);
inline uint32_t& RowU(uint8_t* s, unsigned offset) { return *reinterpret_cast<uint32_t*>(s + offset); }
inline int32_t RowI(uint8_t* s, unsigned offset) { return static_cast<int32_t>(RowU(s, offset)); }
inline uint32_t BlendFour(const uint32_t* p, uint32_t mask, uint32_t x, uint32_t y) {
    uint32_t xy = (x * y) >> 16;
    uint32_t wx = x - xy, wy = y - xy;
    uint32_t w0 = (65536u - wx - wy - xy) >> 8, w1 = wx >> 8, w2 = wy >> 8, w3 = xy >> 8;
    if (mask & 1) w0 = 0; if (mask & 2) w1 = 0; if (mask & 4) w2 = 0; if (mask & 8) w3 = 0;
    // Match the original's packed-lane, unsigned 32-bit arithmetic and truncation.
    uint32_t low = (p[0]&0x00ff00ffu)*w0 + (p[1]&0x00ff00ffu)*w1 + (p[2]&0x00ff00ffu)*w2 + (p[3]&0x00ff00ffu)*w3;
    uint32_t high = ((p[0]>>8)&0x00ff00ffu)*w0 + ((p[1]>>8)&0x00ff00ffu)*w1 + ((p[2]>>8)&0x00ff00ffu)*w2 + ((p[3]>>8)&0x00ff00ffu)*w3;
    return ((low >> 8) & 0x00ff00ffu) | (high & 0xff00ff00u);
}
inline uint32_t* __cdecl FastBilinearRow(int count, uint32_t* output, uint8_t* state) {
    const int32_t left=RowI(state,0x54), top=RowI(state,0x58), right=RowI(state,0x5c), bottom=RowI(state,0x60);
    const uint32_t pitch=RowU(state,0x24), bytesPerPixel=RowU(state,0x20)>>3;
    RowU(state,0xa8) += 0xffff8001u;
    RowU(state,0xac) += 0xffff8001u;
    const int32_t sy=RowI(state,0xac)>>16;
    uint32_t fy=RowU(state,0xac)&0xffffu, verticalMask=0;
    if (sy<top) { verticalMask=sy+1<top?15:3; fy=65535; }
    if (sy+1>=bottom) { verticalMask|=12; if(sy>=bottom)verticalMask=15; fy=0; }
    if(verticalMask==15) { if(count>0)memset(output,0,static_cast<size_t>(count)*4); return output; }
    const uint32_t row=RowU(state,0x28)+static_cast<uint32_t>(sy)*pitch;
    const auto decode=*reinterpret_cast<PixelDecoder*>(state+0xb8);
    const uint32_t step=RowU(state,0x90);
    for(int i=0;i<count;++i) {
        const int32_t sx=RowI(state,0xa8)>>16;
        uint32_t fx=RowU(state,0xa8)&0xffffu, mask=verticalMask;
        bool outside=false;
        if(sx<left) { mask|=5; if(sx+1<left)outside=true; else fx=65535; }
        if(!outside && sx+1>=right) { mask|=10; if(sx>=right)outside=true; else fx=0; }
        if(outside) output[i]=0;
        else {
            const uint32_t address=row+static_cast<uint32_t>(sx)*bytesPerPixel;
            // The game's decoders walk a null-terminated list, not a fixed array of four.
            const uint8_t* source[5] = { reinterpret_cast<const uint8_t*>(address), reinterpret_cast<const uint8_t*>(address+bytesPerPixel),
                reinterpret_cast<const uint8_t*>(address+pitch), reinterpret_cast<const uint8_t*>(address+pitch+bytesPerPixel), nullptr };
            uint32_t pixels[4]{};
            RowU(state,0xb4)=mask;
            decode(source,pixels,state);
            output[i]=RowU(state,0xb4)==15?0:BlendFour(pixels,RowU(state,0xb4),fx,fy);
        }
        RowU(state,0xa8)+=step;
    }
    return output;
}
