#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <cstdint>
#include <cstdio>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

#include <nora_x1/diagnostics.hpp>

using Microsoft::WRL::ComPtr;

namespace
{
    std::wstring vendor_name(const UINT vendor_id)
    {
        switch (vendor_id)
        {
        case 0x1002:
            return L"AMD";
        case 0x10DE:
            return L"NVIDIA";
        case 0x8086:
            return L"Intel";
        default:
            return L"Other";
        }
    }

    std::wstring json_escape(const std::wstring& value)
    {
        std::wstring escaped;
        for (const wchar_t character : value)
        {
            switch (character)
            {
            case L'"':
                escaped += L"\\\"";
                break;
            case L'\\':
                escaped += L"\\\\";
                break;
            case L'\b':
                escaped += L"\\b";
                break;
            case L'\f':
                escaped += L"\\f";
                break;
            case L'\n':
                escaped += L"\\n";
                break;
            case L'\r':
                escaped += L"\\r";
                break;
            case L'\t':
                escaped += L"\\t";
                break;
            default:
                if (character < 0x20)
                {
                    constexpr wchar_t hex[] = L"0123456789abcdef";
                    escaped += L"\\u00";
                    escaped += hex[(character >> 4) & 0x0F];
                    escaped += hex[character & 0x0F];
                }
                else
                {
                    escaped += character;
                }
                break;
            }
        }

        return escaped;
    }

    std::wstring driver_version(const ComPtr<IDXGIAdapter1>& adapter)
    {
        LARGE_INTEGER version{};
        if (SUCCEEDED(adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice), &version)))
        {
            const auto upper = static_cast<std::uint32_t>(version.QuadPart >> 32);
            const auto lower = static_cast<std::uint32_t>(version.QuadPart);
            std::wostringstream result;
            result << HIWORD(upper) << L'.' << LOWORD(upper) << L'.'
                   << HIWORD(lower) << L'.' << LOWORD(lower);
            return result.str();
        }

        return L"Unavailable";
    }

    nora::x1::diagnostics_snapshot collect_diagnostics()
    {
        nora::x1::diagnostics_snapshot snapshot;
        ComPtr<IDXGIFactory1> factory;
        if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))))
        {
            return snapshot;
        }

        snapshot.dxgi_enumeration_available = true;
        for (UINT index = 0;; ++index)
        {
            ComPtr<IDXGIAdapter1> adapter;
            const HRESULT result = factory->EnumAdapters1(index, &adapter);
            if (result == DXGI_ERROR_NOT_FOUND)
            {
                break;
            }
            if (FAILED(result))
            {
                snapshot.dxgi_enumeration_available = false;
                snapshot.adapters.clear();
                break;
            }

            DXGI_ADAPTER_DESC1 description{};
            if (FAILED(adapter->GetDesc1(&description)))
            {
                continue;
            }
            if ((description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
            {
                continue;
            }

            nora::x1::graphics_adapter item;
            item.name = description.Description;
            item.vendor = vendor_name(description.VendorId);
            std::wostringstream device_id;
            device_id << L"0x";
            device_id.width(4);
            device_id.fill(L'0');
            device_id << std::hex << description.VendorId << L":0x";
            device_id.width(4);
            device_id.fill(L'0');
            device_id << std::hex << description.DeviceId;
            item.device_id = device_id.str();
            item.driver_version = driver_version(adapter);
            item.dedicated_video_memory_bytes = description.DedicatedVideoMemory;
            item.direct3d12_available =
                SUCCEEDED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_11_0, __uuidof(ID3D12Device), nullptr));
            snapshot.adapters.push_back(std::move(item));
        }

        return snapshot;
    }

    std::string to_utf8(const std::wstring& value)
    {
        if (value.empty())
        {
            return {};
        }

        const int required = WideCharToMultiByte(
            CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        if (required <= 0)
        {
            return {};
        }

        std::string converted(static_cast<std::size_t>(required), '\0');
        if (WideCharToMultiByte(
                CP_UTF8, WC_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
                converted.data(), required, nullptr, nullptr) != required)
        {
            return {};
        }

        return converted;
    }

    std::wstring to_json(const nora::x1::diagnostics_snapshot& snapshot)
    {
        std::wostringstream json;
        json << L"{\"schemaVersion\":" << nora::x1::diagnostics_schema_version
             << L",\"dxgiEnumerationAvailable\":" << (snapshot.dxgi_enumeration_available ? L"true" : L"false")
             << L",\"adapters\":[";

        for (std::size_t index = 0; index < snapshot.adapters.size(); ++index)
        {
            if (index != 0)
            {
                json << L',';
            }

            const auto& adapter = snapshot.adapters[index];
            json << L"{\"name\":\"" << json_escape(adapter.name)
                 << L"\",\"vendor\":\"" << json_escape(adapter.vendor)
                 << L"\",\"deviceId\":\"" << json_escape(adapter.device_id)
                 << L"\",\"driverVersion\":\"" << json_escape(adapter.driver_version)
                 << L"\",\"dedicatedVideoMemoryBytes\":" << adapter.dedicated_video_memory_bytes
                 << L",\"direct3D12Available\":" << (adapter.direct3d12_available ? L"true" : L"false")
                 << L'}';
        }

        json << L"]}";
        return json.str();
    }

    bool run_self_test()
    {
        if (vendor_name(0x1002) != L"AMD" ||
            vendor_name(0x10DE) != L"NVIDIA" ||
            vendor_name(0x8086) != L"Intel" ||
            vendor_name(0xFFFF) != L"Other" ||
            json_escape(L"quote\" slash\\ newline\n") != L"quote\\\" slash\\\\ newline\\n")
        {
            std::wcerr << L"NORA X1 probe self-test failed.\n";
            return false;
        }

        std::wcout << L"NORA X1 probe self-test passed.\n";
        return true;
    }
}

int wmain(const int argument_count, wchar_t* arguments[])
{
    if (argument_count == 2 && std::wstring(arguments[1]) == L"--self-test")
    {
        return run_self_test() ? 0 : 1;
    }

    if (argument_count > 1 && std::wstring(arguments[1]) != L"--json")
    {
        std::wcerr << L"Usage: nora-x1-probe.exe [--json | --self-test]\n";
        return 2;
    }

    const auto json = to_utf8(to_json(collect_diagnostics()));
    if (json.empty() || std::fwrite(json.data(), 1, json.size(), stdout) != json.size())
    {
        std::wcerr << L"Could not write the NORA X1 diagnostics response.\n";
        return 1;
    }

    std::fputc('\n', stdout);
    return 0;
}
