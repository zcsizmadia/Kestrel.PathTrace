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
 * Private ioctl buffer for WSAIoctl(SIO_TCP_INFO, version=0).
 *
 * We deliberately avoid using the SDK's TCP_INFO_v0 typedef: different MinGW
 * versions may or may not define it in <mstcpip.h>, causing either "unknown
 * type" or "redefinition" errors depending on the toolchain version.
 * Defining our own struct with a unique tag sidesteps both problems.
 *
 * The layout must exactly match the Windows SDK TCP_INFO_v0 binary layout.
 * ULONG64 fields use their natural alignment so the compiler inserts the same
 * implicit padding as the SDK struct (verified against ws2tcpip.h / mstcpip.h).
 */
typedef struct tcpinfo_v0_ioctl_buf {
    DWORD   State;              /* TCPSTATE enum — backed by DWORD            */
    ULONG   Mss;
    ULONG64 ConnectionTimeMs;
    BOOLEAN TimestampsEnabled;
    ULONG   RttUs;
    ULONG   MinRttUs;
    ULONG   BytesInFlight;
    ULONG   Cwnd;
    ULONG   SndWnd;
    ULONG   RcvWnd;
    ULONG   RcvBuf;
    ULONG64 BytesOut;
    ULONG64 BytesIn;
    ULONG   BytesReordered;
    ULONG   BytesRetrans;
    ULONG   FastRetrans;
    ULONG   DupAcksIn;
    ULONG   TimeoutEpisodes;
    UCHAR   SynRetrans;
} tcpinfo_v0_ioctl_buf;

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
     * tcpinfo_v0_ioctl_buf (version == 0) structure.
     */
    DWORD version = 0;
    tcpinfo_v0_ioctl_buf raw;
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
