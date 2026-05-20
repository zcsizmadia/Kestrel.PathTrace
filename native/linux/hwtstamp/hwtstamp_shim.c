/*
 * hwtstamp_shim.c
 *
 * Linux hardware-timestamping native shim for Kestrel.PathTrace.
 *
 * Exposes a stable C ABI for .NET to call via P/Invoke so that the managed
 * transport layer can:
 *
 *   1. Query whether the NIC supports hardware RX/TX timestamping.
 *   2. Configure the NIC's hwtstamp filter via SIOCSHWTSTAMP.
 *   3. Enable SO_TIMESTAMPING on a per-socket basis.
 *   4. Extract three-tuple (SW / HW-legacy / HW-raw) timestamps from
 *      recvmsg() control messages (RX path).
 *   5. Drain TX timestamps from the socket error queue.
 *   6. Sample CLOCK_MONOTONIC / CLOCK_REALTIME / CLOCK_TAI together for
 *      PHC ↔ Stopwatch clock correlation.
 *   7. Open and read PTP Hardware Clock (PHC) devices directly.
 *
 * Build:
 *   cmake -B build -S . -DCMAKE_BUILD_TYPE=Release
 *   cmake --build build
 *
 * Requires:
 *   Linux kernel ≥ 3.17 for SO_TIMESTAMPING with OPT_CMSG.
 *   Linux kernel ≥ 4.13 for SOF_TIMESTAMPING_OPT_PKTINFO.
 *   ethtool support in the NIC driver for ETHTOOL_GET_TS_INFO.
 *   CAP_NET_ADMIN for SIOCSHWTSTAMP (NIC configuration).
 */

#include "hwtstamp_shim.h"

#ifdef __linux__

#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif

#include <errno.h>
#include <fcntl.h>
#include <net/if.h>
#include <netinet/in.h>
#include <stdio.h>
#include <string.h>
#include <sys/ioctl.h>
#include <sys/socket.h>
#include <sys/types.h>
#include <time.h>
#include <unistd.h>

/* Linux-specific headers */
#include <linux/errqueue.h>
#include <linux/ethtool.h>
#include <linux/net_tstamp.h>
#include <linux/ptp_clock.h>
#include <linux/sockios.h>

/* -------------------------------------------------------------------------
 * Internal helpers
 * ---------------------------------------------------------------------- */

/*
 * Convert a struct timespec into hwts_timespec, marking it valid only when
 * at least one of tv_sec/tv_nsec is non-zero.  The kernel zeros out the
 * slots it does not fill in scm_timestamping.
 */
static void ts_to_hwts(const struct timespec* src, hwts_timespec* dst)
{
    dst->tv_sec  = (int64_t)src->tv_sec;
    dst->tv_nsec = (int64_t)src->tv_nsec;
    dst->valid   = (src->tv_sec != 0 || src->tv_nsec != 0) ? 1 : 0;
}

/*
 * Parse the control-message chain from a recvmsg() call and extract any
 * SCM_TIMESTAMPING (= scm_timestamping) message.
 */
static void extract_cmsg_timestamps(struct msghdr* msg, hwts_timestamps* out)
{
    memset(out, 0, sizeof(*out));

    for (struct cmsghdr* cmsg = CMSG_FIRSTHDR(msg);
         cmsg != NULL;
         cmsg = CMSG_NXTHDR(msg, cmsg))
    {
        if (cmsg->cmsg_level != SOL_SOCKET)
        {
            continue;
        }

        if (cmsg->cmsg_type == SCM_TIMESTAMPING)
        {
            /*
             * scm_timestamping contains three struct timespec values:
             *   [0] = software / system clock
             *   [1] = deprecated HW-to-system converted timestamp
             *   [2] = raw hardware clock (PHC)
             */
            if (cmsg->cmsg_len < CMSG_LEN(sizeof(struct scm_timestamping)))
            {
                continue;
            }

            struct scm_timestamping raw;
            memcpy(&raw, CMSG_DATA(cmsg), sizeof(raw));

            ts_to_hwts(&raw.ts[0], &out->sw);
            ts_to_hwts(&raw.ts[1], &out->hw_legacy);
            ts_to_hwts(&raw.ts[2], &out->hw_raw);
        }
    }
}

/* -------------------------------------------------------------------------
 * hwts_query_nic_capabilities
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_query_nic_capabilities(
    const char*    ifname,
    hwts_nic_caps* caps)
{
    if (ifname == NULL || caps == NULL)
    {
        return HWTS_ERR_NULL_ARG;
    }

    memset(caps, 0, sizeof(*caps));
    caps->phc_index = -1;

    /*
     * ETHTOOL_GET_TS_INFO requires a temporary socket for the ioctl.
     * We create a UDP/IPv4 socket purely as a vehicle.
     */
    int fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd < 0)
    {
        return HWTS_ERR_IOCTL;
    }

    struct ethtool_ts_info ts_info;
    memset(&ts_info, 0, sizeof(ts_info));
    ts_info.cmd = ETHTOOL_GET_TS_INFO;

    struct ifreq ifr;
    memset(&ifr, 0, sizeof(ifr));
    strncpy(ifr.ifr_name, ifname, IFNAMSIZ - 1);
    ifr.ifr_data = (char*)&ts_info;

    int ret = ioctl(fd, SIOCETHTOOL, &ifr);
    int saved_errno = errno;
    close(fd);

    if (ret < 0)
    {
        errno = saved_errno;
        return HWTS_ERR_IOCTL;
    }

    caps->so_timestamping_flags = (uint32_t)ts_info.so_timestamping;
    caps->phc_index             = ts_info.phc_index;
    caps->tx_types              = (uint32_t)ts_info.tx_types;
    caps->rx_filters            = (uint32_t)ts_info.rx_filters;

    /* Derive convenience booleans */
    caps->hw_rx_available  = (ts_info.so_timestamping & SOF_TIMESTAMPING_RX_HARDWARE)  ? 1 : 0;
    caps->hw_tx_available  = (ts_info.so_timestamping & SOF_TIMESTAMPING_TX_HARDWARE)  ? 1 : 0;
    caps->sw_rx_available  = (ts_info.so_timestamping & SOF_TIMESTAMPING_RX_SOFTWARE)  ? 1 : 0;
    caps->sw_tx_available  = (ts_info.so_timestamping & SOF_TIMESTAMPING_TX_SOFTWARE)  ? 1 : 0;
    caps->raw_hw_available = (ts_info.so_timestamping & SOF_TIMESTAMPING_RAW_HARDWARE) ? 1 : 0;

    return HWTS_OK;
}

/* -------------------------------------------------------------------------
 * hwts_configure_nic
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_configure_nic(
    const char*      ifname,
    hwts_nic_config* config)
{
    if (ifname == NULL || config == NULL)
    {
        return HWTS_ERR_NULL_ARG;
    }

    int fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd < 0)
    {
        return HWTS_ERR_IOCTL;
    }

    struct hwtstamp_config hwcfg;
    memset(&hwcfg, 0, sizeof(hwcfg));
    hwcfg.tx_type   = config->tx_type;
    hwcfg.rx_filter = config->rx_filter;

    struct ifreq ifr;
    memset(&ifr, 0, sizeof(ifr));
    strncpy(ifr.ifr_name, ifname, IFNAMSIZ - 1);
    ifr.ifr_data = (char*)&hwcfg;

    int ret = ioctl(fd, SIOCSHWTSTAMP, &ifr);
    int saved_errno = errno;
    close(fd);

    if (ret < 0)
    {
        errno = saved_errno;
        return HWTS_ERR_IOCTL;
    }

    /*
     * The kernel writes back the actually-applied configuration.
     * Update the caller's struct so it knows what was actually set.
     */
    config->tx_type   = hwcfg.tx_type;
    config->rx_filter = hwcfg.rx_filter;

    return HWTS_OK;
}

/* -------------------------------------------------------------------------
 * hwts_enable_socket_timestamps
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_enable_socket_timestamps(
    int      fd,
    uint32_t flags)
{
    int opt = (int)flags;
    if (setsockopt(fd, SOL_SOCKET, SO_TIMESTAMPING, &opt, sizeof(opt)) < 0)
    {
        return HWTS_ERR_SOCKET;
    }

    return HWTS_OK;
}

/* -------------------------------------------------------------------------
 * hwts_recvmsg_with_timestamp
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_recvmsg_with_timestamp(
    int             fd,
    void*           buf,
    size_t          buf_len,
    hwts_rx_result* result)
{
    if (result == NULL)
    {
        return HWTS_ERR_NULL_ARG;
    }

    memset(result, 0, sizeof(*result));
    result->bytes_received = -1;

    /*
     * Control message buffer large enough for:
     *   SCM_TIMESTAMPING (3 × struct timespec = 72 bytes)
     *   SCM_TIMESTAMPING_PKTINFO (optional, ~24 bytes)
     *   alignment padding
     */
    char cmsg_buf[256];
    memset(cmsg_buf, 0, sizeof(cmsg_buf));

    struct iovec iov =
    {
        .iov_base = buf,
        .iov_len  = buf_len,
    };

    struct msghdr msg;
    memset(&msg, 0, sizeof(msg));
    msg.msg_iov        = &iov;
    msg.msg_iovlen     = 1;
    msg.msg_control    = cmsg_buf;
    msg.msg_controllen = sizeof(cmsg_buf);

    ssize_t n = recvmsg(fd, &msg, 0);
    if (n < 0)
    {
        result->last_errno = errno;
        return HWTS_ERR_RECVMSG;
    }

    result->bytes_received = n;
    extract_cmsg_timestamps(&msg, &result->timestamps);

    return HWTS_OK;
}

/* -------------------------------------------------------------------------
 * hwts_read_tx_timestamp
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_read_tx_timestamp(
    int              fd,
    hwts_timestamps* timestamps)
{
    if (timestamps == NULL)
    {
        return HWTS_ERR_NULL_ARG;
    }

    memset(timestamps, 0, sizeof(*timestamps));

    /*
     * When SOF_TIMESTAMPING_OPT_TSONLY is set the kernel places a zero-length
     * payload on the error queue together with the timestamp cmsg.  We pass a
     * small data buffer anyway to be safe with drivers that don't set TSONLY.
     */
    char data_buf[32];
    char cmsg_buf[256];
    memset(cmsg_buf, 0, sizeof(cmsg_buf));

    struct iovec iov =
    {
        .iov_base = data_buf,
        .iov_len  = sizeof(data_buf),
    };

    struct msghdr msg;
    memset(&msg, 0, sizeof(msg));
    msg.msg_iov        = &iov;
    msg.msg_iovlen     = 1;
    msg.msg_control    = cmsg_buf;
    msg.msg_controllen = sizeof(cmsg_buf);

    /*
     * MSG_ERRQUEUE drains the error/timestamp queue.  MSG_DONTWAIT avoids
     * blocking if the TX timestamp has not yet been generated by the NIC.
     */
    ssize_t n = recvmsg(fd, &msg, MSG_ERRQUEUE | MSG_DONTWAIT);
    if (n < 0)
    {
        if (errno == EAGAIN || errno == EWOULDBLOCK)
        {
            return HWTS_ERR_NO_TIMESTAMP;
        }
        return HWTS_ERR_RECVMSG;
    }

    extract_cmsg_timestamps(&msg, timestamps);

    if (!timestamps->hw_raw.valid && !timestamps->sw.valid)
    {
        return HWTS_ERR_NO_TIMESTAMP;
    }

    return HWTS_OK;
}

/* -------------------------------------------------------------------------
 * hwts_sample_clocks
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_sample_clocks(
    hwts_clock_sample* sample)
{
    if (sample == NULL)
    {
        return HWTS_ERR_NULL_ARG;
    }

    struct timespec mono, rt, tai, raw;

    clock_gettime(CLOCK_MONOTONIC,     &mono);
    clock_gettime(CLOCK_REALTIME,      &rt);
    clock_gettime(CLOCK_TAI,           &tai);
    clock_gettime(CLOCK_MONOTONIC_RAW, &raw);

    sample->monotonic_ns     = (int64_t)mono.tv_sec * INT64_C(1000000000) + mono.tv_nsec;
    sample->realtime_ns      = (int64_t)rt.tv_sec   * INT64_C(1000000000) + rt.tv_nsec;
    sample->tai_ns           = (int64_t)tai.tv_sec   * INT64_C(1000000000) + tai.tv_nsec;
    sample->raw_monotonic_ns = (int64_t)raw.tv_sec   * INT64_C(1000000000) + raw.tv_nsec;

    return HWTS_OK;
}

/* -------------------------------------------------------------------------
 * hwts_get_socket_ifname
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_get_socket_ifname(
    int    fd,
    char*  ifname,
    size_t ifname_len)
{
    if (ifname == NULL || ifname_len == 0)
    {
        return HWTS_ERR_NULL_ARG;
    }

    ifname[0] = '\0';

    /*
     * First attempt: SO_BINDTODEVICE — works only when the socket was
     * explicitly bound to a device with setsockopt.
     */
    socklen_t optlen = (socklen_t)(ifname_len < IFNAMSIZ ? ifname_len : IFNAMSIZ);
    if (getsockopt(fd, SOL_SOCKET, SO_BINDTODEVICE, ifname, &optlen) == 0
        && ifname[0] != '\0')
    {
        return HWTS_OK;
    }

    /*
     * Second attempt: find the local address the socket is bound to and
     * look up which interface owns that address via SIOCGIFCONF + SIOCGIFADDR.
     */
    struct sockaddr_storage local_addr;
    socklen_t addr_len = sizeof(local_addr);
    if (getsockname(fd, (struct sockaddr*)&local_addr, &addr_len) < 0)
    {
        return HWTS_ERR_IOCTL;
    }

    /*
     * Create a temporary socket to issue SIOCGIFCONF.
     */
    int tmp_fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (tmp_fd < 0)
    {
        return HWTS_ERR_IOCTL;
    }

    /* Retrieve list of interfaces */
    char if_buf[4096];
    struct ifconf ifc;
    ifc.ifc_len = sizeof(if_buf);
    ifc.ifc_buf = if_buf;

    if (ioctl(tmp_fd, SIOCGIFCONF, &ifc) < 0)
    {
        close(tmp_fd);
        return HWTS_ERR_IOCTL;
    }

    struct ifreq* it  = ifc.ifc_req;
    struct ifreq* end = (struct ifreq*)((char*)ifc.ifc_req + ifc.ifc_len);
    int found = 0;

    for (; it != end; ++it)
    {
        if (local_addr.ss_family == AF_INET
            && it->ifr_addr.sa_family == AF_INET)
        {
            struct sockaddr_in* sin_local = (struct sockaddr_in*)&local_addr;
            struct sockaddr_in* sin_if    = (struct sockaddr_in*)&it->ifr_addr;

            if (sin_local->sin_addr.s_addr == sin_if->sin_addr.s_addr)
            {
                strncpy(ifname, it->ifr_name, ifname_len - 1);
                ifname[ifname_len - 1] = '\0';
                found = 1;
                break;
            }
        }
    }

    close(tmp_fd);
    return found ? HWTS_OK : HWTS_ERR_IOCTL;
}

/* -------------------------------------------------------------------------
 * hwts_open_phc
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_open_phc(int phc_index)
{
    if (phc_index < 0)
    {
        errno = EINVAL;
        return -1;
    }

    char path[64];
    snprintf(path, sizeof(path), "/dev/ptp%d", phc_index);
    return open(path, O_RDWR);
}

/* -------------------------------------------------------------------------
 * hwts_read_phc_time
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_read_phc_time(
    int            phc_fd,
    hwts_timespec* ts)
{
    if (ts == NULL)
    {
        return HWTS_ERR_NULL_ARG;
    }

    struct ptp_clock_time pct;
    memset(&pct, 0, sizeof(pct));

    if (ioctl(phc_fd, PTP_CLOCK_GETCAPS, NULL) < 0 && errno == EBADF)
    {
        return HWTS_ERR_IOCTL;
    }

    /*
     * PTP_SYS_OFFSET reads a series of (sys-time, phc-time, sys-time) tuples
     * that allow accurate PHC ↔ system-clock correlation.  For a simple
     * "current PHC time" we use PTP_CLOCK_GETTIME (= PTPCLK_GETTIME on
     * older kernels) via the ioctl PTP_CLOCK_GETCAPS-adjacent path.
     *
     * The portable way is: ioctl(fd, PTP_CLOCK_GETTIME, &pct) but the
     * constant name varies across kernel versions.  We use the raw ioctl
     * number assembled from the kernel ABI macros.
     */
#ifndef PTP_CLOCK_GETTIME
    /* _IOR('=', 1, struct ptp_clock_time) from <linux/ptp_clock.h> */
#  define PTP_CLOCK_GETTIME _IOR('=', 1, struct ptp_clock_time)
#endif

    if (ioctl(phc_fd, PTP_CLOCK_GETTIME, &pct) < 0)
    {
        return HWTS_ERR_IOCTL;
    }

    ts->tv_sec  = (int64_t)pct.sec;
    ts->tv_nsec = (int64_t)pct.nsec;
    ts->valid   = 1;

    return HWTS_OK;
}

#else /* !__linux__ */

/* -------------------------------------------------------------------------
 * Stub implementations for non-Linux targets.
 * The C# layer gates calls behind RuntimeInformation.IsOSPlatform(Linux).
 * ---------------------------------------------------------------------- */

HWTS_EXPORT int HWTS_CALL hwts_query_nic_capabilities(
    const char* ifname, hwts_nic_caps* caps)
{
    (void)ifname; (void)caps;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_configure_nic(
    const char* ifname, hwts_nic_config* config)
{
    (void)ifname; (void)config;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_enable_socket_timestamps(
    int fd, uint32_t flags)
{
    (void)fd; (void)flags;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_recvmsg_with_timestamp(
    int fd, void* buf, size_t buf_len, hwts_rx_result* result)
{
    (void)fd; (void)buf; (void)buf_len; (void)result;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_read_tx_timestamp(
    int fd, hwts_timestamps* timestamps)
{
    (void)fd; (void)timestamps;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_sample_clocks(hwts_clock_sample* sample)
{
    (void)sample;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_get_socket_ifname(
    int fd, char* ifname, size_t ifname_len)
{
    (void)fd; (void)ifname; (void)ifname_len;
    return HWTS_ERR_NOT_LINUX;
}

HWTS_EXPORT int HWTS_CALL hwts_open_phc(int phc_index)
{
    (void)phc_index;
    errno = ENOSYS;
    return -1;
}

HWTS_EXPORT int HWTS_CALL hwts_read_phc_time(int phc_fd, hwts_timespec* ts)
{
    (void)phc_fd; (void)ts;
    return HWTS_ERR_NOT_LINUX;
}

#endif /* __linux__ */
