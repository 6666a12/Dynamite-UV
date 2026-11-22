cmake_minimum_required(VERSION 3.24)
project(uv_video_stream LANGUAGES CXX)

if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8 OR NOT CMAKE_SYSTEM_PROCESSOR MATCHES "^(AMD64|amd64|x86_64|X86_64)$")
    message(FATAL_ERROR "UV Video Stream targets Windows x64 only")
endif()
set(UV_GODOT_CPP "" CACHE PATH "Checkout of the pinned godot-cpp revision (not vendored)")
set(UV_GODOT_API "" CACHE PATH "API/interface JSON dumped by the pinned Godot executable")
find_package(Git REQUIRED)
set(UV_SDK_REV "7fd5c4bc3efda6487fd6e6486c50016faa622f92")
if(NOT EXISTS "${UV_GODOT_CPP}/CMakeLists.txt" OR NOT EXISTS "${UV_GODOT_API}/extension_api.json" OR NOT EXISTS "${UV_GODOT_API}/gdextension_interface.json")
    message(FATAL_ERROR "Set UV_GODOT_CPP and UV_GODOT_API; see README.md")
endif()
execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${UV_GODOT_CPP}" rev-parse HEAD
    RESULT_VARIABLE uv_git_result OUTPUT_VARIABLE uv_sdk_rev OUTPUT_STRIP_TRAILING_WHITESPACE)
if(NOT uv_git_result EQUAL 0 OR NOT uv_sdk_rev STREQUAL UV_SDK_REV)
    message(FATAL_ERROR "Expected godot-cpp ${UV_SDK_REV}, found '${uv_sdk_rev}'")
endif()
execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${UV_GODOT_CPP}" diff HEAD --quiet --
    RESULT_VARIABLE uv_sdk_dirty)
if(NOT uv_sdk_dirty EQUAL 0)
    message(FATAL_ERROR "The pinned godot-cpp checkout has local tracked changes")
endif()
file(SHA256 "${UV_GODOT_API}/extension_api.json" uv_api_hash)
file(SHA256 "${UV_GODOT_API}/gdextension_interface.json" uv_interface_hash)
if(NOT uv_api_hash STREQUAL "0607a5695052f05eeaacac736a2aa68439e05f93e61a1f8e694d889a83cf32ff" OR
   NOT uv_interface_hash STREQUAL "7d8c0a039d9743eb8ebf88681ae0c641d8d3aa5ffca11081745a84da803e09a1")
    message(FATAL_ERROR "The SDK dump must match official Godot 4.7.1.stable.mono a13da4feb (byte-exact Windows JSON dumps)")
endif()
set(GODOTCPP_CUSTOM_API_FILE "${UV_GODOT_API}/extension_api.json" CACHE FILEPATH "" FORCE)
set(GODOTCPP_GDEXTENSION_DIR "${UV_GODOT_API}" CACHE PATH "" FORCE)
set(GODOTCPP_BUILD_PROFILE "${CMAKE_CURRENT_SOURCE_DIR}/build-profile.json" CACHE FILEPATH "" FORCE)
set(GODOTCPP_TARGET "template_release" CACHE STRING "" FORCE)
add_subdirectory("${UV_GODOT_CPP}" _godot_cpp)

add_library(uv_video_stream SHARED movie_writer.cpp matroska.cpp pipe.cpp stream.cpp)
target_compile_features(uv_video_stream PRIVATE cxx_std_17)
option(UV_STREAM_ENABLE_QA "Allow deterministic test-only sink delays from the environment" OFF)
target_compile_definitions(uv_video_stream PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0601
    $<$<BOOL:${UV_STREAM_ENABLE_QA}>:UV_STREAM_ENABLE_QA>)
target_link_libraries(uv_video_stream PRIVATE godot::cpp advapi32)
set_target_properties(uv_video_stream PROPERTIES PREFIX "")
if(MINGW)
    target_compile_options(uv_video_stream PRIVATE -Wall -Wextra -Wpedantic)
    target_link_options(uv_video_stream PRIVATE -static)
elseif(MSVC)
    target_compile_options(uv_video_stream PRIVATE /W4)
    set_property(TARGET uv_video_stream PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()
set(uv_variant_writer "${CMAKE_CURRENT_SOURCE_DIR}/write-variant.cmake")
set(uv_descriptor "${CMAKE_CURRENT_SOURCE_DIR}/uv_video_stream.gdextension")
set_property(TARGET uv_video_stream APPEND PROPERTY LINK_DEPENDS "${uv_variant_writer}" "${uv_descriptor}")
add_custom_command(TARGET uv_video_stream POST_BUILD
    COMMAND "${CMAKE_COMMAND}"
        "-DUV_DLL=$<TARGET_FILE:uv_video_stream>"
        "-DUV_VARIANT=$<IF:$<BOOL:${UV_STREAM_ENABLE_QA}>,qa,release>"
        -P "${uv_variant_writer}"
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different
        "${uv_descriptor}" "$<TARGET_FILE_DIR:uv_video_stream>"
    VERBATIM)

include(CTest)
if(BUILD_TESTING)
    add_executable(uv_stream_test stream_test.cpp matroska.cpp pipe.cpp stream.cpp)
    target_compile_features(uv_stream_test PRIVATE cxx_std_17)
    target_compile_definitions(uv_stream_test PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX _WIN32_WINNT=0x0601)
    target_link_libraries(uv_stream_test PRIVATE advapi32)
    if(MINGW)
        target_compile_options(uv_stream_test PRIVATE -Wall -Wextra -Wpedantic)
        target_link_options(uv_stream_test PRIVATE -static)
    endif()
    add_test(NAME uv_stream_test COMMAND uv_stream_test)
    set_tests_properties(uv_stream_test PROPERTIES TIMEOUT 45)
endif()
