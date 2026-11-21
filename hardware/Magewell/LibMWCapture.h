// Magewell Pro Capture / Eco Capture SDK - Core Types
// Reference: Magewell MWCapture SDK
#pragma once

typedef void* HCHANNEL;

enum MW_VIDEO_CAPTURE_STATUS {
    MW_VIDEO_CAPTURE_OK = 0,
    MW_VIDEO_CAPTURE_NO_SIGNAL = 1,
    MW_VIDEO_CAPTURE_UNSUPPORTED = 2
};

typedef struct _MWCAP_VIDEO_BUFFER_INFO {
    unsigned long dwTotalCount;
    unsigned long dwBufferedCount;
} MWCAP_VIDEO_BUFFER_INFO;
