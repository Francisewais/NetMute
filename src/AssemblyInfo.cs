using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("NetMute")]
[assembly: AssemblyDescription("Per-program network switch for Windows")]
[assembly: AssemblyProduct("NetMute")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Francisewais. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

// NetMute runs elevated, often out of a Downloads folder, so P/Invoke must only ever
// load DLLs from System32 and never something dropped next to the exe.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
