cmake_minimum_required(VERSION 3.24)

project(NoraX1Driver VERSION 0.1.0 LANGUAGES CXX)

include(CTest)

add_library(nora_x1_sdk INTERFACE)
target_include_directories(nora_x1_sdk INTERFACE "${CMAKE_CURRENT_SOURCE_DIR}/SDK/include")
target_compile_features(nora_x1_sdk INTERFACE cxx_std_17)

add_executable(nora-x1-probe src/nora_x1_probe.cpp)
target_link_libraries(nora-x1-probe PRIVATE nora_x1_sdk dxgi d3d12)
target_compile_definitions(nora-x1-probe PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
set_target_properties(nora-x1-probe PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin/$<CONFIG>"
)

if(BUILD_TESTING)
    add_test(NAME nora-x1-probe-self-test COMMAND nora-x1-probe --self-test)
endif()
