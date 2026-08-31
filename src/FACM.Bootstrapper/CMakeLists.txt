cmake_minimum_required(VERSION 3.20)
project(FACMBootstrapper LANGUAGES CXX)

add_executable(FACM WIN32 main.cpp ManifestTrust.cpp)
target_compile_features(FACM PRIVATE cxx_std_17)
target_compile_definitions(FACM PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_link_libraries(FACM PRIVATE user32 shell32 advapi32 bcrypt crypt32 winhttp cabinet)

if(MSVC)
    set_property(TARGET FACM PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(FACM PRIVATE /W4 /WX /EHsc)
else()
    target_compile_options(FACM PRIVATE -Wall -Wextra -Wpedantic -static -static-libgcc -static-libstdc++)
    target_link_options(FACM PRIVATE -static -municode)
endif()

set_target_properties(FACM PROPERTIES OUTPUT_NAME "FACM")
