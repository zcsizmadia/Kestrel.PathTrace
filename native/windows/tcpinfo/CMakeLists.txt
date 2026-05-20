cmake_minimum_required(VERSION 3.20)
project(tcpinfo_shim C)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)

if(NOT WIN32)
    message(FATAL_ERROR "tcpinfo_shim is Windows-only")
endif()

add_library(tcpinfo_shim SHARED
    tcpinfo_shim.c)

target_include_directories(tcpinfo_shim PRIVATE .)

# Link Winsock2
target_link_libraries(tcpinfo_shim PRIVATE ws2_32)

# Export only the symbols declared in the header
set_target_properties(tcpinfo_shim PROPERTIES
    WINDOWS_EXPORT_ALL_SYMBOLS OFF
    PREFIX "")   # prevent MinGW from adding 'lib' prefix → tcpinfo_shim.dll not libtcpinfo_shim.dll

install(TARGETS tcpinfo_shim
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib)
