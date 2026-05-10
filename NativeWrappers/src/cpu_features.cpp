#include <cstdint>
#include <cstring>
#include <iostream>
#include <fstream>
#include <sstream>
#include <unordered_map>

#include "cpu_features.h"

#include <string>

#define PROCESSOR_FEATURE_MAX           64

static bool initialized = false;

static bool ProcessorFeatures[PROCESSOR_FEATURE_MAX];

int32_t has_processor_feature(uint32_t feature) {
    return feature < PROCESSOR_FEATURE_MAX && ProcessorFeatures[feature];
}

// Admitted defeat and parsing /proc/cpuinfo
static std::unordered_map<std::string, uint32_t> flagToFeatureMap = {
    {"cx8", PF_COMPARE_EXCHANGE_DOUBLE},
    {"mmx", PF_MMX_INSTRUCTIONS_AVAILABLE},
    {"sse", PF_XMMI_INSTRUCTIONS_AVAILABLE},
    {"3dnow", PF_3DNOW_INSTRUCTIONS_AVAILABLE},
    {"tsc", PF_RDTSC_INSTRUCTION_AVAILABLE},
    {"pae", PF_PAE_ENABLED},
    {"sse2", PF_XMMI64_INSTRUCTIONS_AVAILABLE},
    {"sse3", PF_SSE3_INSTRUCTIONS_AVAILABLE},
    {"ssse3", PF_SSSE3_INSTRUCTIONS_AVAILABLE},
    {"xsave", PF_XSAVE_ENABLED},
    {"cx16", PF_COMPARE_EXCHANGE128}, // Compare Exchange 128-bit
    {"daz", PF_SSE_DAZ_MODE_AVAILABLE},
    {"nx", PF_NX_ENABLED},
    {"virt", PF_VIRT_FIRMWARE_ENABLED},
    {"rdfsgsbase", PF_RDWRFSGSBASE_AVAILABLE},
    {"sse4_1", PF_SSE4_1_INSTRUCTIONS_AVAILABLE},
    {"sse4_2", PF_SSE4_2_INSTRUCTIONS_AVAILABLE},
    {"avx", PF_AVX_INSTRUCTIONS_AVAILABLE},
    {"avx2", PF_AVX2_INSTRUCTIONS_AVAILABLE}
};

bool parseCPUInfo() {
    if (initialized)
        return true;

    memset(&ProcessorFeatures, 0, sizeof(ProcessorFeatures));

    std::ifstream cpuinfo("/proc/cpuinfo");
    if (!cpuinfo.is_open()) {
        std::cerr << "Error opening /proc/cpuinfo" << std::endl;
        return false;
    }

    std::string line;
    while (std::getline(cpuinfo, line)) {
        if (line.find("flags") != std::string::npos) {
            std::string flags_str = line.substr(line.find(":") + 2);
            std::istringstream flags_stream(flags_str);
            std::string flag;

            while (flags_stream >> flag) {
                auto it = flagToFeatureMap.find(flag);
                if (it != flagToFeatureMap.end()) {
                    ProcessorFeatures[it->second] = true;
                }
            }
            break; // We only need to parse the first set of flags
        }
    }

    // Special case for FASTFAIL_AVAILABLE
    ProcessorFeatures[PF_FASTFAIL_AVAILABLE] = true;

    initialized = true;
    return true;
}

