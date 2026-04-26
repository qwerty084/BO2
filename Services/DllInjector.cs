using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BO2.Services
{
    public sealed class DllInjector
    {
        private const string MonitorDllFileName = "BO2Monitor.dll";
        private const uint Infinite = 0xffffffff;
        private const uint WaitObject0 = 0;
        private const uint MemCommit = 0x00001000;
        private const uint MemReserve = 0x00002000;
        private const uint MemRelease = 0x00008000;
        private const uint PageReadWrite = 0x04;
        private const uint ProcessCreateThread = 0x0002;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmWrite = 0x0020;
        private const uint ProcessVmRead = 0x0010;
        private static readonly uint InjectionProcessAccess =
            ProcessCreateThread
            | ProcessQueryInformation
            | ProcessVmOperation
            | ProcessVmWrite
            | ProcessVmRead;

        public DllInjectionResult Inject(DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                return DllInjectionResult.NotAttempted;
            }

            if (detectedGame.Variant != GameVariant.SteamZombies || !detectedGame.IsStatsSupported)
            {
                return new DllInjectionResult(
                    DllInjectionState.UnsupportedGame,
                    AppStrings.Format("DllInjectionUnsupportedGameFormat", detectedGame.DisplayName));
            }

            string dllPath = ResolveMonitorPath();
            if (!File.Exists(dllPath))
            {
                return new DllInjectionResult(
                    DllInjectionState.MonitorDllMissing,
                    AppStrings.Format("DllInjectionMissingDllFormat", dllPath),
                    dllPath);
            }

            try
            {
                if (IsMonitorAlreadyLoaded(detectedGame.ProcessId))
                {
                    return new DllInjectionResult(
                        DllInjectionState.AlreadyInjected,
                        AppStrings.Get("DllInjectionAlreadyLoaded"),
                        dllPath);
                }

                if (Environment.Is64BitProcess)
                {
                    InjectLibraryViaWow64PowerShell(detectedGame.ProcessId, dllPath);
                }
                else
                {
                    InjectLibrary(detectedGame.ProcessId, dllPath);
                }

                return new DllInjectionResult(
                    DllInjectionState.Injected,
                    AppStrings.Get("DllInjectionSucceeded"),
                    dllPath);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return new DllInjectionResult(
                    DllInjectionState.Failed,
                    AppStrings.Format("DllInjectionFailedFormat", ex.Message),
                    dllPath);
            }
        }

        internal static string ResolveMonitorPath()
        {
            return Path.Combine(AppContext.BaseDirectory, MonitorDllFileName);
        }

        internal static string ResolveWow64PowerShellPath()
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return Path.Combine(windowsDirectory, "SysWOW64", "WindowsPowerShell", "v1.0", "powershell.exe");
        }

        private static bool IsMonitorAlreadyLoaded(int processId)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, MonitorDllFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch (Win32Exception) when (Environment.Is64BitProcess)
            {
                // Cross-bitness module enumeration can fail. Calling LoadLibraryW
                // for an already-loaded DLL just returns the existing module.
                return false;
            }

            return false;
        }

        private static void InjectLibrary(int processId, string dllPath)
        {
            byte[] dllPathBytes = Encoding.Unicode.GetBytes(dllPath + '\0');
            IntPtr processHandle = OpenProcess(InjectionProcessAccess, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                throw CreateWin32Exception("OpenProcess");
            }

            IntPtr remotePathAddress = IntPtr.Zero;
            IntPtr threadHandle = IntPtr.Zero;
            try
            {
                remotePathAddress = VirtualAllocEx(
                    processHandle,
                    IntPtr.Zero,
                    (UIntPtr)dllPathBytes.Length,
                    MemCommit | MemReserve,
                    PageReadWrite);
                if (remotePathAddress == IntPtr.Zero)
                {
                    throw CreateWin32Exception("VirtualAllocEx");
                }

                if (!WriteProcessMemory(
                    processHandle,
                    remotePathAddress,
                    dllPathBytes,
                    dllPathBytes.Length,
                    out UIntPtr bytesWritten)
                    || bytesWritten.ToUInt64() != (ulong)dllPathBytes.Length)
                {
                    throw CreateWin32Exception("WriteProcessMemory");
                }

                IntPtr kernel32Handle = GetModuleHandle("kernel32.dll");
                if (kernel32Handle == IntPtr.Zero)
                {
                    throw CreateWin32Exception("GetModuleHandle");
                }

                IntPtr loadLibraryAddress = GetProcAddress(kernel32Handle, "LoadLibraryW");
                if (loadLibraryAddress == IntPtr.Zero)
                {
                    throw CreateWin32Exception("GetProcAddress");
                }

                threadHandle = CreateRemoteThread(
                    processHandle,
                    IntPtr.Zero,
                    0,
                    loadLibraryAddress,
                    remotePathAddress,
                    0,
                    IntPtr.Zero);
                if (threadHandle == IntPtr.Zero)
                {
                    throw CreateWin32Exception("CreateRemoteThread");
                }

                uint waitResult = WaitForSingleObject(threadHandle, Infinite);
                if (waitResult != WaitObject0)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionWaitFailedFormat", waitResult));
                }

                if (!GetExitCodeThread(threadHandle, out uint exitCode) || exitCode == 0)
                {
                    throw CreateWin32Exception("GetExitCodeThread");
                }
            }
            finally
            {
                if (threadHandle != IntPtr.Zero)
                {
                    CloseHandle(threadHandle);
                }

                if (remotePathAddress != IntPtr.Zero)
                {
                    VirtualFreeEx(processHandle, remotePathAddress, UIntPtr.Zero, MemRelease);
                }

                CloseHandle(processHandle);
            }
        }

        private static void InjectLibraryViaWow64PowerShell(int processId, string dllPath)
        {
            string powerShellPath = ResolveWow64PowerShellPath();
            if (!File.Exists(powerShellPath))
            {
                throw new InvalidOperationException(AppStrings.Get("DllInjectionWrongArchitecture"));
            }

            string escapedDllPath = dllPath.Replace("'", "''", StringComparison.Ordinal);
            string script = $$"""
$ErrorActionPreference = 'Stop'
$source = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public static class RemoteDllInjector
{
    private const uint PROCESS_CREATE_THREAD = 0x0002;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint INFINITE = 0xffffffff;
    private const uint WAIT_OBJECT_0 = 0;

    public static void Inject(int processId, string dllPath)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(dllPath + '\0');
        IntPtr processHandle = OpenProcess(
            PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
            false,
            processId);
        if (processHandle == IntPtr.Zero)
        {
            ThrowLastError("OpenProcess");
        }

        IntPtr remotePath = IntPtr.Zero;
        IntPtr threadHandle = IntPtr.Zero;
        try
        {
            remotePath = VirtualAllocEx(processHandle, IntPtr.Zero, (UIntPtr)bytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remotePath == IntPtr.Zero)
            {
                ThrowLastError("VirtualAllocEx");
            }

            UIntPtr written;
            if (!WriteProcessMemory(processHandle, remotePath, bytes, bytes.Length, out written)
                || written.ToUInt64() != (ulong)bytes.Length)
            {
                ThrowLastError("WriteProcessMemory");
            }

            IntPtr loadLibrary = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero)
            {
                ThrowLastError("LoadLibraryW");
            }

            threadHandle = CreateRemoteThread(processHandle, IntPtr.Zero, 0, loadLibrary, remotePath, 0, IntPtr.Zero);
            if (threadHandle == IntPtr.Zero)
            {
                ThrowLastError("CreateRemoteThread");
            }

            uint waitResult = WaitForSingleObject(threadHandle, INFINITE);
            if (waitResult != WAIT_OBJECT_0)
            {
                throw new InvalidOperationException("Remote loader wait failed with result " + waitResult);
            }

            uint exitCode;
            if (!GetExitCodeThread(threadHandle, out exitCode) || exitCode == 0)
            {
                ThrowLastError("GetExitCodeThread");
            }
        }
        finally
        {
            if (threadHandle != IntPtr.Zero)
            {
                CloseHandle(threadHandle);
            }

            if (remotePath != IntPtr.Zero)
            {
                VirtualFreeEx(processHandle, remotePath, UIntPtr.Zero, MEM_RELEASE);
            }

            CloseHandle(processHandle);
        }
    }

    private static void ThrowLastError(string apiName)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), apiName);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out UIntPtr bytesWritten);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr moduleHandle, string procName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize, IntPtr start, IntPtr parameter, uint flags, IntPtr threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
'@
Add-Type -TypeDefinition $source
[RemoteDllInjector]::Inject({{processId}}, '{{escapedDllPath}}')
""";
            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(AppStrings.Get("DllInjectionWrongArchitecture"));
            string standardOutput = process.StandardOutput.ReadToEnd();
            string standardError = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                }

                throw new InvalidOperationException(AppStrings.Format("DllInjectionWaitFailedFormat", "timeout"));
            }

            if (process.ExitCode != 0)
            {
                string message = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                throw new InvalidOperationException(message.Trim());
            }
        }

        private static Win32Exception CreateWin32Exception(string apiName)
        {
            return new Win32Exception(Marshal.GetLastWin32Error(), apiName);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr processHandle,
            IntPtr address,
            UIntPtr size,
            uint allocationType,
            uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(
            IntPtr processHandle,
            IntPtr address,
            UIntPtr size,
            uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(
            IntPtr processHandle,
            IntPtr baseAddress,
            byte[] buffer,
            int size,
            out UIntPtr numberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr moduleHandle, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr processHandle,
            IntPtr threadAttributes,
            uint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
