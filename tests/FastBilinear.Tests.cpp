#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "../src/Backend/LoginHandler/FastBilinear.h"
#include <cstdio>
#include <vector>
#include <random>
#include <string>

void __cdecl Decode(const uint8_t** source,uint32_t* output,uint8_t* state) {
    for(unsigned i=0;source[i];++i) {
        if(i>=4) { puts("{\"error\":\"missing_decoder_sentinel\"}"); ExitProcess(6); }
        if(RowU(state,0xb4)&(1u<<i)) { output[i]=0; continue; }
        unsigned bytes=RowU(state,0x20)>>3;
        uint32_t pixel=0; memcpy(&pixel,source[i],bytes); output[i]=pixel;
        // Exercise decoders that mark color-key pixels invalid as well as clip edges.
        if((pixel&31u)==RowU(state,0x70)) RowU(state,0xb4)|=1u<<i;
    }
}
int main(int argc,char** argv) {
    if(argc!=2)return 2;
    SetDllDirectoryA(argv[1]);
    std::string file=std::string(argv[1])+"\\randy31.dll";
    auto base=reinterpret_cast<uint8_t*>(LoadLibraryExA(file.c_str(),nullptr,LOAD_WITH_ALTERED_SEARCH_PATH));
    if(!base){printf("{\"error\":\"load_failed\",\"code\":%lu}\n",GetLastError());return 3;}
    const uint8_t expected[]={0x55,0x8b,0xec,0x83,0xec,0x60,0x53,0x8b,0x5d,0x10};
    if(memcmp(base+0x6bfc1,expected,sizeof(expected))){puts("{\"error\":\"signature_mismatch\"}");return 4;}
    auto original=reinterpret_cast<BilinearRow>(base+0x6bfc1);
    std::mt19937 random(732749);
    std::vector<uint8_t> pixels(128*128*4);
    for(auto& p:pixels)p=static_cast<uint8_t>(random());
    uint32_t reference[512], candidate[512];
    int64_t referenceTicks=0,candidateTicks=0; uint64_t checkedPixels=0;
    for(unsigned test=0;test<100000;++test) {
        alignas(16) uint8_t state[0xbc]{}, copy[0xbc]{};
        unsigned bpp=1+random()%4, width=2+random()%126, height=2+random()%126;
        RowU(state,0x20)=bpp*8; RowU(state,0x24)=width*bpp;
        RowU(state,0x28)=reinterpret_cast<uintptr_t>(pixels.data());
        RowU(state,0x54)=random()%(width/2); RowU(state,0x58)=random()%(height/2);
        RowU(state,0x5c)=width; RowU(state,0x60)=height; RowU(state,0x70)=random()%40;
        RowU(state,0x90)=random()%180000;
        RowU(state,0xa8)=random()%((width+6)*65536)-3*65536;
        RowU(state,0xac)=random()%((height+6)*65536)-3*65536;
        *reinterpret_cast<PixelDecoder*>(state+0xb8)=Decode;
        memcpy(copy,state,sizeof(state));
        int count=1+random()%512;
        LARGE_INTEGER t0,t1,t2; QueryPerformanceCounter(&t0);
        auto r=original(count,reference,state); QueryPerformanceCounter(&t1);
        auto c=FastBilinearRow(count,candidate,copy); QueryPerformanceCounter(&t2);
        referenceTicks+=t1.QuadPart-t0.QuadPart; candidateTicks+=t2.QuadPart-t1.QuadPart;
        if(r!=reference||c!=candidate||memcmp(reference,candidate,count*4)||memcmp(state,copy,sizeof(state))) {
            printf("{\"passed\":false,\"test\":%u,\"pixelsMatch\":%s,\"stateMatches\":%s}\n",test,
              memcmp(reference,candidate,count*4)?"false":"true",memcmp(state,copy,sizeof(state))?"false":"true");return 5;
        }
        checkedPixels+=count;
    }
    LARGE_INTEGER frequency; QueryPerformanceFrequency(&frequency);
    printf("{\"passed\":true,\"cases\":100000,\"pixels\":%llu,\"referenceMs\":%.3f,\"candidateMs\":%.3f}\n",
        checkedPixels,referenceTicks*1000.0/frequency.QuadPart,candidateTicks*1000.0/frequency.QuadPart);
    return 0;
}
