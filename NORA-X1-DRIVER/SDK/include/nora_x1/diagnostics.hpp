#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace nora::x1
{
    inline constexpr std::uint32_t diagnostics_schema_version = 1;

    struct graphics_adapter
    {
        std::wstring name;
        std::wstring vendor;
        std::wstring device_id;
        std::wstring driver_version;
        std::uint64_t dedicated_video_memory_bytes = 0;
        bool direct3d12_available = false;
    };

    struct diagnostics_snapshot
    {
        std::vector<graphics_adapter> adapters;
        bool dxgi_enumeration_available = false;
    };
}
