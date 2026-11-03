using System.Runtime.InteropServices;

// All P/Invokes in this assembly use Windows system libraries (including the System32 NVML driver).
// The embedded WebView2 loader has its own absolute-path, hash and held-handle verification.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
