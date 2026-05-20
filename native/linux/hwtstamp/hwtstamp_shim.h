#pragma once

#ifndef HWTSTAMP_SHIM_H
#define HWTSTAMP_SHIM_H

#include <stdint.h>
#include <stddef.h>
#include <sys/types.h>   /* ssize_t */

#ifdef __cplusplus
extern "C" {
#endif

/* -------------------------------------------------------------------------
 * Visibility / calling-convention macros
 * ---------------------------------------------------------------------- */
#if defined(__GNUC__) || defined(__clang__)
#  define HWTS_EXPORT __attribute__((visibility("default")))
#  define HWTS_CALL
#else
#  define HWTS_EXPORT
#  define HWTS_CALL
#endif

/* -------------------------------------------------------------------------
 * Error codes returned by this library (negative to avoid clashing with errno)
 * ---------------------------------------------------------------------- */
#define HWTS_OK                  0
#define HWTS_ERR_NULL_ARG       -1   /* NULL pointer passed to an out param   */
#define HWTS_ERR_UNSUPPORTED    -2   /* OS or NIC does not support feature     */
#define HWTS_ERR_IOCTL          -3   /* ioctl failed; check errno             */
#define HWTS_ERR_SOCKET         -4   /* setsockopt/getsockopt failed          */
#define HWTS_ERR_RECVMSG        -5   /* recvmsg failed                        */
#define HWTS_ERR_NO_TIMESTAMP   -6   /* recvmsg succeeded but no TS in cmsg   */
#define HWTS_ERR_BUF_TOO_SMALL  -7   /* caller-supplied buffer too small      */
#define HWTS_ERR_NOT_LINUX      -8   /* compiled for non-Linux target         */

/* -------------------------------------------------------------------------
 * NIC hardware-timestamping capabilities
 * (mirrors ethtool_ts_info from <linux/ethtool.h>)
 * ---------------------------------------------------------------------- */
typedef struct hwts_nic_caps
{
    /*
     * Raw SO_TIMESTAMPING flags bitmask the driver reports as supported.
     * Inspect individual bits with the HWTS_SO_FLAG_* constants below.
     */
    uint32_t so_timestamping_flags;

    /*
     * PTP clock device index (phc_index).  -1 means no PTP clock.
     * Use /dev/ptp<phc_index> to access the NIC's hardware clock.
     */
    int32_t  phc_index;

    /*
     * Bitmask of supported TX timestamp types (hwtstamp_tx_types enum).
     * Common values:
     *   HWTSTAMP_TX_OFF      = 0
     *   HWTSTAMP_TX_ON       = 1
     *   HWTSTAMP_TX_ONESTEP_SYNC = 2
     */
    uint32_t tx_types;

    /*
     * Bitmask of supported RX filter modes (hwtstamp_rx_filters enum).
     * Common values:
     *   HWTSTAMP_FILTER_NONE        = 0
     *   HWTSTAMP_FILTER_ALL         = 1
     *   HWTSTAMP_FILTER_SOME        = 2
     *   HWTSTAMP_FILTER_PTP_V1_L4_EVENT = 3
     *   ...
     */
    uint32_t rx_filters;

    /* Derived convenience booleans ---------------------------------------- */
    uint8_t  hw_rx_available;   /* 1 if SOF_TIMESTAMPING_RX_HARDWARE is set   */
    uint8_t  hw_tx_available;   /* 1 if SOF_TIMESTAMPING_TX_HARDWARE is set   */
    uint8_t  sw_rx_available;   /* 1 if SOF_TIMESTAMPING_RX_SOFTWARE is set   */
    uint8_t  sw_tx_available;   /* 1 if SOF_TIMESTAMPING_TX_SOFTWARE is set   */
    uint8_t  raw_hw_available;  /* 1 if SOF_TIMESTAMPING_RAW_HARDWARE is set  */
} hwts_nic_caps;

/* SOF_TIMESTAMPING bit positions (from <linux/net_tstamp.h>) */
#define HWTS_SO_FLAG_TX_HARDWARE  (1u << 0)   /* SOF_TIMESTAMPING_TX_HARDWARE  */
#define HWTS_SO_FLAG_TX_SOFTWARE  (1u << 1)   /* SOF_TIMESTAMPING_TX_SOFTWARE  */
#define HWTS_SO_FLAG_RX_HARDWARE  (1u << 2)   /* SOF_TIMESTAMPING_RX_HARDWARE  */
#define HWTS_SO_FLAG_RX_SOFTWARE  (1u << 3)   /* SOF_TIMESTAMPING_RX_SOFTWARE  */
#define HWTS_SO_FLAG_SOFTWARE     (1u << 4)   /* SOF_TIMESTAMPING_SOFTWARE     */
#define HWTS_SO_FLAG_SYS_HARDWARE (1u << 5)   /* SOF_TIMESTAMPING_SYS_HARDWARE (deprecated) */
#define HWTS_SO_FLAG_RAW_HARDWARE (1u << 6)   /* SOF_TIMESTAMPING_RAW_HARDWARE */
#define HWTS_SO_FLAG_OPT_ID       (1u << 7)   /* SOF_TIMESTAMPING_OPT_ID       */
#define HWTS_SO_FLAG_OPT_TSONLY   (1u << 11)  /* SOF_TIMESTAMPING_OPT_TSONLY   */
#define HWTS_SO_FLAG_OPT_CMSG     (1u << 10)  /* SOF_TIMESTAMPING_OPT_CMSG     */
#define HWTS_SO_FLAG_OPT_STATS    (1u << 12)  /* SOF_TIMESTAMPING_OPT_STATS    */
#define HWTS_SO_FLAG_OPT_PKTINFO  (1u << 13)  /* SOF_TIMESTAMPING_OPT_PKTINFO  */
#define HWTS_SO_FLAG_OPT_TX_SWHW (1u << 14)  /* SOF_TIMESTAMPING_OPT_TX_SWHW  */

/* -------------------------------------------------------------------------
 * NIC hardware-timestamping configuration applied via SIOCSHWTSTAMP
 * ---------------------------------------------------------------------- */
typedef struct hwts_nic_config
{
    /*
     * TX timestamp type (hwtstamp_tx_types).
     * Pass HWTS_TX_OFF (0) to disable TX timestamping.
     */
    int32_t  tx_type;

    /*
     * RX filter (hwtstamp_rx_filters).
     * Pass HWTS_RX_FILTER_NONE (0) to disable, HWTS_RX_FILTER_ALL (1) to
     * timestamp every received packet.
     */
    int32_t  rx_filter;
} hwts_nic_config;

/* Convenience constants matching kernel hwtstamp_tx_types / hwtstamp_rx_filters */
#define HWTS_TX_OFF                     0
#define HWTS_TX_ON                      1
#define HWTS_TX_ONESTEP_SYNC            2

#define HWTS_RX_FILTER_NONE             0
#define HWTS_RX_FILTER_ALL              1
#define HWTS_RX_FILTER_SOME            2
#define HWTS_RX_FILTER_PTP_V1_L4_EVENT 3
#define HWTS_RX_FILTER_PTP_V2_L4_EVENT 9
#define HWTS_RX_FILTER_PTP_V2_EVENT    12

/* -------------------------------------------------------------------------
 * A single timestamp (nanoseconds since epoch, or since PHC epoch for HW)
 * ---------------------------------------------------------------------- */
typedef struct hwts_timespec
{
    int64_t  tv_sec;    /* seconds           */
    int64_t  tv_nsec;   /* nanoseconds [0, 999999999] */
    uint8_t  valid;     /* 1 = populated, 0 = not available */
} hwts_timespec;

/* -------------------------------------------------------------------------
 * Three-tuple returned by scm_timestamping:
 *   sw       — CLOCK_REALTIME software timestamp
 *   hw_legacy— deprecated (legacy HW-to-system conversion, usually zero)
 *   hw_raw   — raw PHC/NIC hardware clock timestamp (TAI or PHC epoch)
 * ---------------------------------------------------------------------- */
typedef struct hwts_timestamps
{
    hwts_timespec sw;          /* SOF_TIMESTAMPING_SOFTWARE / _RX_SOFTWARE   */
    hwts_timespec hw_legacy;   /* SOF_TIMESTAMPING_SYS_HARDWARE (deprecated) */
    hwts_timespec hw_raw;      /* SOF_TIMESTAMPING_RAW_HARDWARE              */
} hwts_timestamps;

/* -------------------------------------------------------------------------
 * Clock sample used for correlating PHC nanoseconds with CLOCK_MONOTONIC.
 * .NET's Stopwatch uses CLOCK_MONOTONIC on Linux; hardware timestamps come
 * from the PHC (which tracks TAI or is free-running).  Callers should sample
 * both clocks together, then compute the offset once at startup.
 * ---------------------------------------------------------------------- */
typedef struct hwts_clock_sample
{
    int64_t monotonic_ns;   /* CLOCK_MONOTONIC nanoseconds                   */
    int64_t realtime_ns;    /* CLOCK_REALTIME  nanoseconds                   */
    int64_t tai_ns;         /* CLOCK_TAI       nanoseconds (TAI ≈ UTC+37 s)  */
    int64_t raw_monotonic_ns; /* CLOCK_MONOTONIC_RAW nanoseconds             */
} hwts_clock_sample;

/* -------------------------------------------------------------------------
 * Receive-message result: data bytes read + the extracted timestamps
 * ---------------------------------------------------------------------- */
typedef struct hwts_rx_result
{
    ssize_t          bytes_received;   /* payload bytes (−1 on error)        */
    hwts_timestamps  timestamps;
    int              last_errno;       /* errno if bytes_received < 0        */
} hwts_rx_result;

/* =========================================================================
 * Public API
 * ====================================================================== */

/*
 * Query the hardware-timestamping capabilities of a network interface.
 *
 * ifname  — interface name, e.g. "eth0", "ens3"
 * caps    — output; filled on HWTS_OK
 *
 * Returns HWTS_OK, or a negative HWTS_ERR_* code.
 * On HWTS_ERR_IOCTL the caller may inspect errno for the underlying error.
 */
HWTS_EXPORT int HWTS_CALL hwts_query_nic_capabilities(
    const char*    ifname,
    hwts_nic_caps* caps);

/*
 * Configure hardware timestamping on a network interface.
 *
 * Requires CAP_NET_ADMIN.  The kernel will apply the nearest supported mode
 * and write the actually-configured values back into *config.
 *
 * ifname  — interface name
 * config  — in/out; desired configuration on entry, actual configuration on exit
 *
 * Returns HWTS_OK, or a negative HWTS_ERR_* code.
 */
HWTS_EXPORT int HWTS_CALL hwts_configure_nic(
    const char*     ifname,
    hwts_nic_config* config);

/*
 * Enable SO_TIMESTAMPING on a socket descriptor.
 *
 * fd    — a connected or bound TCP/UDP socket
 * flags — bitmask of HWTS_SO_FLAG_* bits to enable
 *
 * A sensible default for HW RX + SW fallback:
 *   HWTS_SO_FLAG_RX_HARDWARE | HWTS_SO_FLAG_RAW_HARDWARE |
 *   HWTS_SO_FLAG_RX_SOFTWARE | HWTS_SO_FLAG_SOFTWARE
 *
 * Returns HWTS_OK, or a negative HWTS_ERR_* code.
 */
HWTS_EXPORT int HWTS_CALL hwts_enable_socket_timestamps(
    int      fd,
    uint32_t flags);

/*
 * Perform a single recvmsg() on fd and extract any attached timestamps.
 *
 * fd       — socket descriptor with SO_TIMESTAMPING already enabled
 * buf      — caller-supplied data buffer
 * buf_len  — size of buf in bytes
 * result   — output; always written (bytes_received = -1 on error)
 *
 * Returns HWTS_OK on success (even if no HW timestamp was present; check
 * result->timestamps.hw_raw.valid).
 * Returns HWTS_ERR_RECVMSG if recvmsg() itself failed.
 */
HWTS_EXPORT int HWTS_CALL hwts_recvmsg_with_timestamp(
    int             fd,
    void*           buf,
    size_t          buf_len,
    hwts_rx_result* result);

/*
 * Read a TX hardware timestamp from the socket error queue.
 *
 * After calling send/write on a socket that has SOF_TIMESTAMPING_TX_HARDWARE
 * (and optionally SOF_TIMESTAMPING_OPT_TSONLY) enabled, the kernel places
 * the TX timestamp on the socket's error queue.  Call this function after
 * poll()/select() indicates POLLERR on the socket.
 *
 * fd         — socket descriptor
 * timestamps — output; filled on HWTS_OK
 *
 * Returns HWTS_OK, HWTS_ERR_NO_TIMESTAMP, or HWTS_ERR_RECVMSG.
 */
HWTS_EXPORT int HWTS_CALL hwts_read_tx_timestamp(
    int              fd,
    hwts_timestamps* timestamps);

/*
 * Sample multiple Linux clocks atomically (best-effort; three consecutive
 * clock_gettime() calls with no lock).
 *
 * Use this once at startup to compute the PHC ↔ CLOCK_MONOTONIC offset so
 * that hardware timestamps can be correlated with .NET Stopwatch ticks.
 *
 * sample — output; always written
 *
 * Returns HWTS_OK.
 */
HWTS_EXPORT int HWTS_CALL hwts_sample_clocks(
    hwts_clock_sample* sample);

/*
 * Retrieve the network interface name associated with a connected socket.
 *
 * fd        — connected socket descriptor
 * ifname    — caller-supplied buffer; filled with '\0'-terminated interface name
 * ifname_len— size of ifname buffer in bytes (IFNAMSIZ = 16 is sufficient)
 *
 * Returns HWTS_OK, or HWTS_ERR_IOCTL if the interface cannot be determined.
 *
 * Note: uses SO_BINDTODEVICE getsockopt first; if the socket is not bound to
 * a device it falls back to routing-table lookup via SIOCGIFADDR + /proc.
 */
HWTS_EXPORT int HWTS_CALL hwts_get_socket_ifname(
    int    fd,
    char*  ifname,
    size_t ifname_len);

/*
 * Open the PTP Hardware Clock device for the NIC identified by phc_index.
 *
 * phc_index — value from hwts_nic_caps.phc_index (must be >= 0)
 *
 * Returns a file descriptor (>= 0) on success, or -1 on error (check errno).
 * The caller is responsible for close()ing the descriptor.
 */
HWTS_EXPORT int HWTS_CALL hwts_open_phc(int phc_index);

/*
 * Read the current time from an open PTP Hardware Clock descriptor.
 *
 * phc_fd  — file descriptor returned by hwts_open_phc()
 * ts      — output; the raw PHC clock value
 *
 * Returns HWTS_OK, or HWTS_ERR_IOCTL on failure.
 */
HWTS_EXPORT int HWTS_CALL hwts_read_phc_time(
    int           phc_fd,
    hwts_timespec* ts);

#ifdef __cplusplus
}
#endif

#endif /* HWTSTAMP_SHIM_H */
