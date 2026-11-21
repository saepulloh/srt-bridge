// AJA Video Systems NTV2 SDK - Core Enums & Device Types
// Reference: libajantv2 (https://github.com/aja-video/libajantv2)
#pragma once

enum NTV2DeviceID {
    DEVICE_ID_KONA4     = 0x10544B34,
    DEVICE_ID_KONA5     = 0x10624B35,
    DEVICE_ID_CORVID44  = 0x10554334,
    DEVICE_ID_CORVID88  = 0x10604338
};

enum NTV2VideoFormat {
    NTV2_FORMAT_1080p_5000 = 85,
    NTV2_FORMAT_1080p_5994 = 86,
    NTV2_FORMAT_1080p_6000 = 87,
    NTV2_FORMAT_3840x2160p_5000 = 143,
    NTV2_FORMAT_3840x2160p_5994 = 144,
    NTV2_FORMAT_3840x2160p_6000 = 145
};

enum NTV2Channel {
    NTV2_CHANNEL1 = 0,
    NTV2_CHANNEL2 = 1,
    NTV2_CHANNEL3 = 2,
    NTV2_CHANNEL4 = 3,
    NTV2_CHANNEL5 = 4,
    NTV2_CHANNEL6 = 5,
    NTV2_CHANNEL7 = 6,
    NTV2_CHANNEL8 = 7
};
