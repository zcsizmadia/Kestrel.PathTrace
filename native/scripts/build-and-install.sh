#!/usr/bin/env bash
# build-and-install.sh — build the platform-specific native shim and install
# it to runtimes/<RID>/native/ at the repository root.
#
# Usage:
#   ./native/scripts/build-and-install.sh [Release|Debug]
#
# Supported RIDs:
#   linux-x64  linux-arm64  linux-musl-x64  linux-musl-arm64
#   win-x64    win-arm64
#
# Prerequisites: cmake, a C compiler (gcc / clang / MSVC / MinGW)
# The CMake generator is not forced — CMake picks the best available one.

set -x
set -euo pipefail

printenv

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
BUILD_CONFIG="${1:-Release}"

# ── RID detection ──────────────────────────────────────────────────────────────

detect_os() {
    case "$(uname -s)" in
        Linux*)              echo "linux" ;;
        MINGW*|MSYS*|CYGWIN*) echo "win"  ;;
        *)
            echo "error: unsupported OS '$(uname -s)'" >&2
            exit 1 ;;
    esac
}

detect_arch() {
    # Inside a VS Developer Command Prompt, VSCMD_ARG_TGT_ARCH is more reliable
    # than uname -m (e.g. when the host shell bitness differs from the target).
    if [ -n "${VSCMD_ARG_TGT_ARCH:-}" ]; then
        case "${VSCMD_ARG_TGT_ARCH}" in
            x64)   echo "x64"   ; return ;;
            arm64) echo "arm64" ; return ;;
        esac
    fi
    # RUNNER_ARCH is set by Github runner
    if [ -n "${RUNNER_ARCH:-}" ]; then
        case "${RUNNER_ARCH}" in
            ARM64) echo "arm64" ; return ;;
            AMD64) echo "x64"   ; return ;;
            X64) echo "x64"     ; return ;;
        esac
    fi
    # PROCESSOR_ARCHITECTURE is set by Windows for every process and correctly
    # reflects the native machine architecture even when Git Bash (an x64 app)
    # runs under ARM64 emulation and uname -m returns x86_64.
    if [ -n "${PROCESSOR_ARCHITECTURE:-}" ]; then
        case "${PROCESSOR_ARCHITECTURE}" in
            ARM64) echo "arm64" ; return ;;
            AMD64) echo "x64"   ; return ;;
        esac
    fi
    case "$(uname -m)" in
        x86_64|amd64)  echo "x64"   ;;
        aarch64|arm64) echo "arm64" ;;
        *)
            echo "error: unsupported architecture '$(uname -m)'" >&2
            exit 1 ;;
    esac
}

is_musl() {
    # Alpine ships /etc/alpine-release; fall back to checking ldd output.
    [ -f /etc/alpine-release ] && return 0
    command -v ldd >/dev/null 2>&1 \
        && ldd /bin/sh 2>/dev/null | grep -qi musl \
        && return 0
    return 1
}

OS="$(detect_os)"
ARCH="$(detect_arch)"
MUSL=""

if [ "$OS" = "linux" ] && is_musl; then
    MUSL="-musl"
fi

RID="${OS}${MUSL}-${ARCH}"

# ── Select native source ───────────────────────────────────────────────────────

case "$OS" in
    linux)
        NATIVE_SRC="${REPO_ROOT}/native/linux/hwtstamp"
        LIB_FILE="libhwtstamp_shim.so"
        ;;
    win)
        NATIVE_SRC="${REPO_ROOT}/native/windows/tcpinfo"
        LIB_FILE="tcpinfo_shim.dll"
        ;;
esac

echo "RID    : ${RID}"
echo "Config : ${BUILD_CONFIG}"
echo "Source : ${NATIVE_SRC}"
echo ""

# ── CMake generator selection ──────────────────────────────────────────────────
CMAKE_EXTRA_ARGS=()
if [ "$OS" = "win" ]; then
    case "$ARCH" in
        x64)   CMAKE_EXTRA_ARGS+=("-A" "x64")   ;;
        arm64) CMAKE_EXTRA_ARGS+=("-A" "ARM64")  ;;
    esac
    echo "Generator: Visual Studio (arch=${ARCH})"
fi

# ── CMake configure + build ────────────────────────────────────────────────────

BUILD_DIR="${REPO_ROOT}/native/build/${RID}"
mkdir -p "${BUILD_DIR}"

cmake -S "${NATIVE_SRC}" -B "${BUILD_DIR}" -DCMAKE_BUILD_TYPE="${BUILD_CONFIG}" "${CMAKE_EXTRA_ARGS[@]}"
cmake --build "${BUILD_DIR}" --config "${BUILD_CONFIG}" --parallel

# ── Locate the built library ───────────────────────────────────────────────────
# Single-config generators (Ninja, Unix Makefiles) place the output directly
# in the build dir.  Multi-config generators (MSVC) place it under {Config}/.

BUILT_LIB="${BUILD_DIR}/${LIB_FILE}"
if [ ! -f "${BUILT_LIB}" ]; then
    BUILT_LIB="${BUILD_DIR}/${BUILD_CONFIG}/${LIB_FILE}"
fi

if [ ! -f "${BUILT_LIB}" ]; then
    echo "error: built library not found under ${BUILD_DIR}" >&2
    exit 1
fi

# ── Install to runtimes/<RID>/native/ ─────────────────────────────────────────

INSTALL_DIR="${REPO_ROOT}/runtimes/${RID}/native"
mkdir -p "${INSTALL_DIR}"
cp -v "${BUILT_LIB}" "${INSTALL_DIR}/${LIB_FILE}"

echo ""
echo "Installed: ${INSTALL_DIR}/${LIB_FILE}"
