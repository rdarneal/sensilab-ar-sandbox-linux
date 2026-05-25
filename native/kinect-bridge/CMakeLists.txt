cmake_minimum_required(VERSION 3.16)
project(kinect_bridge LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 14)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Hide everything by default; explicit KB_API exports remain visible.
add_compile_options(-Wall -Wextra -fvisibility=hidden -O2)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)
set(CMAKE_C_VISIBILITY_PRESET hidden)
set(CMAKE_VISIBILITY_INLINES_HIDDEN ON)

set(FREENECT2_ROOT "$ENV{HOME}/freenect2")
set(FREENECT2_INCLUDE "${FREENECT2_ROOT}/include")
set(FREENECT2_LIB "${FREENECT2_ROOT}/lib/libfreenect2.so")

if(NOT EXISTS "${FREENECT2_INCLUDE}/libfreenect2/libfreenect2.hpp")
    message(FATAL_ERROR "libfreenect2 headers not found at ${FREENECT2_INCLUDE}. Build libfreenect2 with CMAKE_INSTALL_PREFIX=$ENV{HOME}/freenect2 first.")
endif()
if(NOT EXISTS "${FREENECT2_LIB}")
    message(FATAL_ERROR "libfreenect2.so not found at ${FREENECT2_LIB}.")
endif()

find_package(Threads REQUIRED)

add_library(kinectbridge SHARED
    src/kinect_bridge.cpp
)
target_include_directories(kinectbridge
    PUBLIC  ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${FREENECT2_INCLUDE}
)
target_link_libraries(kinectbridge
    PRIVATE
        ${FREENECT2_LIB}
        Threads::Threads
)

# RPATH with $ORIGIN first so a deployed build finds libfreenect2.so.0.2
# next to libkinectbridge.so (scripts/build-bridge.sh stages it there, and
# Unity packages both into <Build>_Data/Plugins/x86_64/ on a Linux build).
# FREENECT2_ROOT/lib stays as a build-time fallback so the smoke test still
# resolves the dep when run directly from build/ where libfreenect2 isn't
# present.
set_target_properties(kinectbridge PROPERTIES
    OUTPUT_NAME "kinectbridge"
    BUILD_RPATH "\$ORIGIN:${FREENECT2_ROOT}/lib"
    INSTALL_RPATH "\$ORIGIN:${FREENECT2_ROOT}/lib"
)

add_executable(smoke_test test/smoke_test.c)
target_include_directories(smoke_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/include)
target_link_libraries(smoke_test PRIVATE kinectbridge)
set_target_properties(smoke_test PROPERTIES
    BUILD_RPATH "${CMAKE_CURRENT_BINARY_DIR};${FREENECT2_ROOT}/lib"
)
