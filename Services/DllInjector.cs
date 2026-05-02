using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BO2.Services
{
    public sealed class DllInjector
    {
        private const string MonitorDllFileName = "BO2Monitor.dll";
        private const string InjectorHelperFileName = "BO2InjectorHelper.exe";
        private const string StartMonitorExportName = "StartMonitor";
        private const ushort ImageFileMachineI386 = 0x014c;
        private const uint DontResolveDllReferences = 0x00000001;
        private const uint WaitObject0 = 0;
        private const uint WaitTimeout = 0x00000102;
        private const uint RemoteThreadTimeoutMilliseconds = 15000;
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
        private readonly Func<bool> _is64BitProcess;
        private readonly Func<string> _resolveMonitorPath;
        private readonly Func<string, bool> _fileExists;
        private readonly Func<string, DllPayloadValidationResult> _validateMonitorDll;
        private readonly Func<int, bool> _isMonitorAlreadyLoaded;
        private readonly Action<int, string> _injectLibrary;
        private readonly Action<int, string> _injectLibraryViaWow64Helper;

        public DllInjector()
            : this(
                () => Environment.Is64BitProcess,
                ResolveMonitorPath,
                File.Exists,
                ValidateMonitorDll,
                IsMonitorAlreadyLoaded,
                InjectLibrary,
                InjectLibraryViaWow64Helper)
        {
        }

        internal DllInjector(
            Func<bool> is64BitProcess,
            Func<string> resolveMonitorPath,
            Func<string, bool> fileExists,
            Func<string, DllPayloadValidationResult> validateMonitorDll,
            Func<int, bool> isMonitorAlreadyLoaded,
            Action<int, string> injectLibrary,
            Action<int, string> injectLibraryViaWow64Helper)
        {
            _is64BitProcess = is64BitProcess;
            _resolveMonitorPath = resolveMonitorPath;
            _fileExists = fileExists;
            _validateMonitorDll = validateMonitorDll;
            _isMonitorAlreadyLoaded = isMonitorAlreadyLoaded;
            _injectLibrary = injectLibrary;
            _injectLibraryViaWow64Helper = injectLibraryViaWow64Helper;
        }

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

            string dllPath = _resolveMonitorPath();
            if (!_fileExists(dllPath))
            {
                return new DllInjectionResult(
                    DllInjectionState.MonitorDllMissing,
                    AppStrings.Format("DllInjectionMissingDllFormat", dllPath),
                    dllPath);
            }

            DllPayloadValidationResult validationResult = _validateMonitorDll(dllPath);
            if (!validationResult.IsValid)
            {
                return new DllInjectionResult(
                    DllInjectionState.Failed,
                    AppStrings.Format(
                        "DllInjectionInvalidDllFormat",
                        validationResult.Message ?? AppStrings.Get("EventMonitorUnknown")),
                    dllPath);
            }

            try
            {
                bool monitorAlreadyLoaded = _isMonitorAlreadyLoaded(detectedGame.ProcessId);

                if (_is64BitProcess())
                {
                    _injectLibraryViaWow64Helper(detectedGame.ProcessId, dllPath);
                }
                else
                {
                    _injectLibrary(detectedGame.ProcessId, dllPath);
                }

                return new DllInjectionResult(
                    monitorAlreadyLoaded ? DllInjectionState.AlreadyInjected : DllInjectionState.Loaded,
                    monitorAlreadyLoaded ? AppStrings.Get("DllInjectionAlreadyLoaded") : AppStrings.Get("DllInjectionLoaded"),
                    dllPath);
            }
            catch (WrongProcessArchitectureException ex)
            {
                return new DllInjectionResult(
                    DllInjectionState.WrongProcessArchitecture,
                    AppStrings.Format("DllInjectionWrongArchitectureFormat", ex.Message),
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
            return Path.GetFullPath(Path.Join(AppContext.BaseDirectory, MonitorDllFileName));
        }

        internal static string ResolveWow64HelperPath()
        {
            return Path.GetFullPath(Path.Join(AppContext.BaseDirectory, InjectorHelperFileName));
        }

        internal static DllPayloadValidationResult ValidateMonitorDll(string dllPath)
        {
            return ValidatePayloadFile(
                dllPath,
                ImageFileMachineI386,
                "DllInjectionInvalidDllFormat",
                "DllInjectionInvalidPathFormat",
                "DllInjectionInvalidMachineFormat");
        }

        internal static DllPayloadValidationResult ValidateInjectorHelper(string helperPath)
        {
            return ValidatePayloadFile(
                helperPath,
                ImageFileMachineI386,
                "DllInjectionInvalidHelperFormat",
                "DllInjectionInvalidHelperPathFormat",
                "DllInjectionInvalidHelperMachineFormat");
        }

        private static DllPayloadValidationResult ValidatePayloadFile(
            string payloadPath,
            ushort expectedMachine,
            string invalidPayloadFormatResourceId,
            string invalidPathFormatResourceId,
            string invalidMachineFormatResourceId)
        {
            string fullPayloadPath = Path.GetFullPath(payloadPath);
            string appBaseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
            if (!appBaseDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                appBaseDirectory += Path.DirectorySeparatorChar;
            }

            if (!fullPayloadPath.StartsWith(appBaseDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return DllPayloadValidationResult.Invalid(
                    AppStrings.Format(invalidPathFormatResourceId, fullPayloadPath));
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(fullPayloadPath);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    return DllPayloadValidationResult.Invalid(
                        AppStrings.Format(invalidPayloadFormatResourceId, fullPayloadPath));
                }

                if (!HasExpectedPeMachine(fullPayloadPath, expectedMachine))
                {
                    return DllPayloadValidationResult.Invalid(
                        AppStrings.Format(invalidMachineFormatResourceId, fullPayloadPath));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                return DllPayloadValidationResult.Invalid(ex.Message);
            }

            return DllPayloadValidationResult.Valid;
        }

        internal static bool HasExpectedPeMachine(string path, ushort expectedMachine)
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
            if (stream.Length < 0x40)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
            }

            if (reader.ReadUInt16() != 0x5A4D)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
            }

            stream.Position = 0x3C;
            int peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset <= 0 || peHeaderOffset > stream.Length - 6)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
            }

            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
            }

            ushort machine = reader.ReadUInt16();
            return machine == expectedMachine;
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

                uint waitResult = WaitForSingleObject(threadHandle, RemoteThreadTimeoutMilliseconds);
                if (waitResult != WaitObject0)
                {
                    if (waitResult == WaitTimeout)
                    {
                        // The remote loader thread may still read this buffer after our wait expires.
                        remotePathAddress = IntPtr.Zero;
                    }

                    throw new InvalidOperationException(AppStrings.Format("DllInjectionWaitFailedFormat", waitResult));
                }

                if (!GetExitCodeThread(threadHandle, out uint exitCode) || exitCode == 0)
                {
                    throw CreateWin32Exception("GetExitCodeThread");
                }

                CloseHandle(threadHandle);
                threadHandle = IntPtr.Zero;
                StartRemoteMonitor(processHandle, dllPath, new IntPtr(unchecked((int)exitCode)));
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

        private static void StartRemoteMonitor(IntPtr processHandle, string dllPath, IntPtr remoteModuleHandle)
        {
            IntPtr startMonitorAddress = ResolveRemoteExportAddress(
                dllPath,
                remoteModuleHandle,
                StartMonitorExportName);
            IntPtr threadHandle = CreateRemoteThread(
                processHandle,
                IntPtr.Zero,
                0,
                startMonitorAddress,
                IntPtr.Zero,
                0,
                IntPtr.Zero);
            if (threadHandle == IntPtr.Zero)
            {
                throw CreateWin32Exception("CreateRemoteThread");
            }

            try
            {
                uint waitResult = WaitForSingleObject(threadHandle, RemoteThreadTimeoutMilliseconds);
                if (waitResult != WaitObject0)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionStartFailedFormat", waitResult));
                }

                if (!GetExitCodeThread(threadHandle, out uint exitCode) || exitCode == 0)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionStartFailedFormat", exitCode));
                }
            }
            finally
            {
                CloseHandle(threadHandle);
            }
        }

        private static IntPtr ResolveRemoteExportAddress(string dllPath, IntPtr remoteModuleHandle, string exportName)
        {
            IntPtr localModuleHandle = LoadLibraryEx(dllPath, IntPtr.Zero, DontResolveDllReferences);
            if (localModuleHandle == IntPtr.Zero)
            {
                throw CreateWin32Exception("LoadLibraryEx");
            }

            try
            {
                IntPtr localExportAddress = GetProcAddress(localModuleHandle, exportName);
                if (localExportAddress == IntPtr.Zero)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName));
                }

                long exportOffset = localExportAddress.ToInt64() - localModuleHandle.ToInt64();
                if (exportOffset < 0)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName));
                }

                return new IntPtr(remoteModuleHandle.ToInt64() + exportOffset);
            }
            finally
            {
                FreeLibrary(localModuleHandle);
            }
        }

        private static void InjectLibraryViaWow64Helper(int processId, string dllPath)
        {
            string helperPath = ResolveWow64HelperPath();
            if (!File.Exists(helperPath))
            {
                throw new WrongProcessArchitectureException(AppStrings.Get("DllInjectionMissingHelper"));
            }

            DllPayloadValidationResult validationResult = ValidateInjectorHelper(helperPath);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException(AppStrings.Format(
                    "DllInjectionInvalidHelperFormat",
                    validationResult.Message ?? AppStrings.Get("EventMonitorUnknown")));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = helperPath,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add(dllPath);

            using Process process = Process.Start(startInfo)
                ?? throw new WrongProcessArchitectureException(AppStrings.Get("DllInjectionMissingHelper"));
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    Debug.WriteLine(ex);
                }

                throw new InvalidOperationException(AppStrings.Format("DllInjectionWaitFailedFormat", "timeout"));
            }

            string standardOutput = standardOutputTask.GetAwaiter().GetResult();
            string standardError = standardErrorTask.GetAwaiter().GetResult();
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

        internal sealed record DllPayloadValidationResult(bool IsValid, string? Message)
        {
            public static DllPayloadValidationResult Valid { get; } = new(true, null);

            public static DllPayloadValidationResult Invalid(string message)
            {
                return new DllPayloadValidationResult(false, message);
            }
        }

        internal sealed class WrongProcessArchitectureException(string message) : InvalidOperationException(message)
        {
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr moduleHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        // DLL injection must resolve exports from the native payload; no managed API provides this.
        // codeql[cs/unmanaged-code]
        private static extern IntPtr GetProcAddress(
            IntPtr moduleHandle,
            [MarshalAs(UnmanagedType.LPStr)] string procName);

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
