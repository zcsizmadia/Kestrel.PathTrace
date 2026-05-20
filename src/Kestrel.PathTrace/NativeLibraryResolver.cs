using System.Reflection;
using System.Runtime.InteropServices;

namespace Kestrel.PathTrace;

/// <summary>
/// Registers a <see cref="NativeLibrary"/> resolver for the native shims
/// shipped with this assembly.
///
/// The standard .NET P/Invoke probing relies on <c>deps.json</c>-derived
/// <c>NATIVE_DLL_SEARCH_DIRECTORIES</c>, which works for <c>dotnet run</c>
/// and <c>dotnet publish -r &lt;RID&gt;</c> but can fail when the assembly
/// is loaded via a custom <see cref="System.Runtime.Loader.AssemblyLoadContext"/>,
/// inside a plugin host, or in non-standard deployments.
///
/// This resolver adds two extra probe locations <em>before</em> the default
/// fallback so those scenarios work without configuration:
/// <list type="number">
///   <item>Next to the assembly file itself (covers self-contained publish
///         and the local dev <c>CopyToOutputDirectory</c> copy).</item>
///   <item><c>runtimes/&lt;RID&gt;/native/</c> relative to the assembly
///         (covers framework-dependent NuGet layouts extracted beside the
///         package DLL).</item>
/// </list>
///
/// Returning <see cref="IntPtr.Zero"/> from the resolver causes the runtime
/// to continue with its built-in probing, so the NuGet package-cache path
/// (set via <c>NATIVE_DLL_SEARCH_DIRECTORIES</c>) still acts as a final
/// fallback.
/// </summary>
internal static class NativeLibraryResolver
{
    private static int _registered;

    /// <summary>
    /// Ensures the resolver is registered exactly once for this assembly.
    /// Call from the static constructor of each platform interop class so
    /// registration happens before the first P/Invoke, regardless of which
    /// platform is active.
    /// </summary>
    internal static void EnsureRegistered()
    {
        if (Interlocked.CompareExchange(ref _registered, 1, 0) == 0)
        {
            NativeLibrary.SetDllImportResolver(
                typeof(NativeLibraryResolver).Assembly,
                Resolve);
        }
    }

    private static IntPtr Resolve(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        string? fileName = GetFileName(libraryName);

        // Unknown library — let the runtime use its default probing.
        if (fileName is null)
        {
            return IntPtr.Zero;
        }

        // 1. Directory that contains the managed assembly.
        //    Covers: local dev (CopyToOutputDirectory), self-contained publish,
        //    and any scenario where the native file was placed beside the DLL.
        string assemblyDir = Path.GetDirectoryName(assembly.Location)
                             ?? AppContext.BaseDirectory;

        if (TryLoad(Path.Combine(assemblyDir, fileName), out IntPtr handle))
        {
            return handle;
        }

        // 2. runtimes/<RID>/native/ relative to the assembly.
        //    Covers: NuGet layouts where native assets are extracted into the
        //    package directory rather than copied to the app root.
        string ridRelative = Path.Combine(
            assemblyDir,
            "runtimes",
            RuntimeInformation.RuntimeIdentifier,
            "native",
            fileName);

        if (TryLoad(ridRelative, out handle))
        {
            return handle;
        }

        // Fall through to the runtime's default probing
        // (deps.json NATIVE_DLL_SEARCH_DIRECTORIES, LD_LIBRARY_PATH, PATH…).
        return IntPtr.Zero;
    }

    /// <summary>
    /// Maps a P/Invoke library name to the platform-specific file name,
    /// or <see langword="null"/> when the library is not one of our own shims.
    /// </summary>
    private static string? GetFileName(string libraryName) => libraryName switch
    {
        "hwtstamp_shim" when OperatingSystem.IsLinux()   => "libhwtstamp_shim.so",
        "tcpinfo_shim"  when OperatingSystem.IsWindows() => "tcpinfo_shim.dll",
        _                                                 => null,
    };

    private static bool TryLoad(string path, out IntPtr handle)
    {
        if (File.Exists(path))
        {
            return NativeLibrary.TryLoad(path, out handle);
        }

        handle = IntPtr.Zero;
        return false;
    }
}
