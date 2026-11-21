// Blackmagic DeckLink SDK - C++ COM Header Definitions & Constants
// Reference: Blackmagic DeckLink SDK 12.x / 14.x
#pragma once
#include <stdint.h>

#define BMDDeckLinkVideoIOSupport 0x76696F73
#define BMDDeckLinkPersistentID   0x706F6964
#define BMDDeckLinkDeviceGroupId  0x67727069

enum _BMDVideoIOSupport {
    bmdDeviceSupportsCapture   = 1 << 0,
    bmdDeviceSupportsPlayback  = 1 << 1
};

enum _BMDDisplayMode {
    bmdModeHD1080p50   = 0x48703530,
    bmdModeHD1080p5994 = 0x48703539,
    bmdModeHD1080p6000 = 0x48703630,
    bmdMode4K2160p50   = 0x346B3530,
    bmdMode4K2160p5994 = 0x346B3539,
    bmdMode4K2160p60   = 0x346B3630
};

enum _BMDPixelFormat {
    bmdFormat8BitYUV      = 0x32767579, // '2vuy'
    bmdFormat10BitYUV     = 0x76323130, // 'v210'
    bmdFormat8BitARGB     = 32,
    bmdFormat8BitBGRA     = 0x42475241  // 'BGRA'
};
