cmake_minimum_required(VERSION 3.20)
project(hwtstamp_shim C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

# Only Linux is fully supported; a stub compiles everywhere else.
if(NOT UNIX)
    message(WARNING
        "hwtstamp_shim: full hardware-timestamping support requires Linux. "
        "Compiling stub-only build.")
endif()

add_library(hwtstamp_shim SHARED
    hwtstamp_shim.c)

target_include_directories(hwtstamp_shim PRIVATE .)

# Compiler flags — optimise for low-overhead inline helpers
target_compile_options(hwtstamp_shim PRIVATE
    -O2
    -Wall
    -Wextra
    -Wpedantic
    -fvisibility=hidden)     # only HWTS_EXPORT symbols are public

# On Linux link against librt for clock_gettime on glibc < 2.17
if(CMAKE_SYSTEM_NAME STREQUAL "Linux")
    target_link_libraries(hwtstamp_shim PRIVATE rt)
endif()

set_target_properties(hwtstamp_shim PROPERTIES
    OUTPUT_NAME  hwtstamp_shim
    SOVERSION    1
    VERSION      1.0.0)

install(TARGETS hwtstamp_shim
    LIBRARY DESTINATION lib
    RUNTIME DESTINATION bin)

install(FILES hwtstamp_shim.h DESTINATION include)
