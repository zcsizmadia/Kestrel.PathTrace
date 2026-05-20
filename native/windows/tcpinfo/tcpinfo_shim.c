/*
 * tcpinfo_shim.c
 *
 * Windows-only native shim that exposes per-socket TCP_INFO_v0 metrics to .NET
 * via a stable C ABI.  The .NET runtime calls get_tcp_info_v0() through P/Invoke.
 *
 * Build:
 *   cmake -B build -S . -DCMAKE_BUILD_TYPE=Release
 *   cmake --build build --config Release
 */

#include "tcpinfo_shim.h"

#ifdef _WIN32

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif

#include <winsock2.h>
#include <ws2tcpip.h>
#include <mstcpip.h>
#include <string.h>

#pragma comment(lib, "Ws2_32.lib")

/*
 * SIO_TCP_INFO control code (available on Windows 10 RS2 / Server 2016+).
 * The version parameter selects v0 (0) or v1 (1).
 */
#ifndef SIO_TCP_INFO
#define SIO_TCP_INFO _WSAIOR(IOC_VENDOR, 26)
#endif

TCPINFO_EXPORT int TCPINFO_CALL get_tcp_info_v0(
    uintptr_t socket_handle,
    tcp_info_v0_shim* info)
{
    if (info == NULL)
    {
        return WSAEFAULT;
    }

    SOCKET s = (SOCKET)socket_handle;

    /*
     * SIO_TCP_INFO takes a DWORD version as input and returns a
     * TCP_INFO_v0 (version == 0) or TCP_INFO_v1 (version == 1) structure.
     */
    DWORD version = 0;
    TCP_INFO_v0 raw;
    DWORD bytes_returned = 0;

    int result = WSAIoctl(
        s,
        SIO_TCP_INFO,
        &version, sizeof(version),
        &raw, sizeof(raw),
        &bytes_returned,
        NULL,
        NULL);

    if (result == SOCKET_ERROR)
    {
        return WSAGetLastError();
    }

    /*
     * Copy individual fields so the layout is ABI-stable regardless of
     * Windows SDK differences.
     */
    info->State             = (uint32_t)raw.State;
    info->Mss               = (uint32_t)raw.Mss;
    info->ConnectionTimeMs  = (uint32_t)raw.ConnectionTimeMs;
    info->TimestampsEnabled = (uint32_t)raw.TimestampsEnabled;
    info->RttUs             = (uint32_t)raw.RttUs;
    info->MinRttUs          = (uint32_t)raw.MinRttUs;
    info->BytesInFlight     = (uint32_t)raw.BytesInFlight;
    info->Cwnd              = (uint32_t)raw.Cwnd;
    info->SndWnd            = (uint32_t)raw.SndWnd;
    info->RcvWnd            = (uint32_t)raw.RcvWnd;
    info->RcvBuf            = (uint32_t)raw.RcvBuf;
    info->BytesOut          = (uint32_t)raw.BytesOut;
    info->BytesIn           = (uint32_t)raw.BytesIn;
    info->BytesReordered    = (uint32_t)raw.BytesReordered;
    info->BytesRetrans      = (uint32_t)raw.BytesRetrans;
    info->FastRetrans       = (uint32_t)raw.FastRetrans;
    info->DupAcksIn         = (uint32_t)raw.DupAcksIn;
    info->TimeoutEpisodes   = (uint32_t)raw.TimeoutEpisodes;
    info->SynRetrans        = (uint8_t)raw.SynRetrans;

    return 0;
}

#else /* !_WIN32 */

/*
 * Stub for non-Windows targets: this library is Windows-only.
 * The C# layer gates calls behind RuntimeInformation.IsOSPlatform(OSPlatform.Windows).
 */
TCPINFO_EXPORT int TCPINFO_CALL get_tcp_info_v0(
    uintptr_t socket_handle,
    tcp_info_v0_shim* info)
{
    (void)socket_handle;
    (void)info;
    return -1; /* ENOSYS equivalent */
}

#endif /* _WIN32 */
