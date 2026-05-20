#pragma once

#ifndef TCPINFO_SHIM_H
#define TCPINFO_SHIM_H

#ifdef _WIN32
#define TCPINFO_EXPORT __declspec(dllexport)
#define TCPINFO_CALL   __stdcall
#else
#define TCPINFO_EXPORT __attribute__((visibility("default")))
#define TCPINFO_CALL
#endif

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Mirrors the Windows TCP_INFO_v0 structure exposed via SIO_TCP_INFO.
 * All size fields use uint32_t to match the Windows DWORD type.
 */
typedef struct tcp_info_v0_shim
{
    uint32_t State;
    uint32_t Mss;
    uint32_t ConnectionTimeMs;
    uint32_t TimestampsEnabled;
    uint32_t RttUs;
    uint32_t MinRttUs;
    uint32_t BytesInFlight;
    uint32_t Cwnd;
    uint32_t SndWnd;
    uint32_t RcvWnd;
    uint32_t RcvBuf;
    uint32_t BytesOut;
    uint32_t BytesIn;
    uint32_t BytesReordered;
    uint32_t BytesRetrans;
    uint32_t FastRetrans;
    uint32_t DupAcksIn;
    uint32_t TimeoutEpisodes;
    uint8_t  SynRetrans;
} tcp_info_v0_shim;

/*
 * Queries TCP_INFO_v0 for the given socket handle via SIO_TCP_INFO.
 *
 * Returns 0 on success, or a WSA error code on failure.
 * The socket handle must be a valid TCP socket (AF_INET or AF_INET6).
 */
TCPINFO_EXPORT int TCPINFO_CALL get_tcp_info_v0(
    uintptr_t socket_handle,
    tcp_info_v0_shim* info);

#ifdef __cplusplus
}
#endif

#endif /* TCPINFO_SHIM_H */
