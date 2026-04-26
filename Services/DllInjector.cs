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

            if (Environment.Is64BitProcess)
            {
                return new DllInjectionResult(
                    DllInjectionState.WrongProcessArchitecture,
                    AppStrings.Get("DllInjectionWrongArchitecture"));
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

                InjectLibrary(detectedGame.ProcessId, dllPath);
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

        private static bool IsMonitorAlreadyLoaded(int processId)
        {
            using Process process = Process.GetProcessById(processId);
            foreach (ProcessModule module in process.Modules)
            {
                if (string.Equals(module.ModuleName, MonitorDllFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
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
