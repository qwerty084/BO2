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
        private const ushort Pe32Magic = 0x010b;
        private const int PeSignatureSize = 4;
        private const int ImageFileHeaderSize = 20;
        private const int Pe32ExportDirectoryOffset = 96;
        private const int ExportDirectoryEntrySize = 8;
        private const int ExportDirectorySize = 40;
        private const int SectionHeaderSize = 40;
        private const int UInt16Size = 2;
        private const int UInt32Size = 4;
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
            // Event Monitor injection needs a remote-thread-capable handle to the supported BO2 process.
            // codeql[cs/call-to-unmanaged-code]
            IntPtr processHandle = OpenProcess(InjectionProcessAccess, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                throw CreateWin32Exception("OpenProcess");
            }

            IntPtr remotePathAddress = IntPtr.Zero;
            IntPtr threadHandle = IntPtr.Zero;
            try
            {
                // The remote process must own the LoadLibraryW argument buffer.
                // codeql[cs/call-to-unmanaged-code]
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

                // The remote LoadLibraryW call consumes the validated absolute monitor DLL path.
                // codeql[cs/call-to-unmanaged-code]
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

                // The remote loader thread entry point must be resolved from kernel32.
                // codeql[cs/call-to-unmanaged-code]
                IntPtr kernel32Handle = GetModuleHandle("kernel32.dll");
                if (kernel32Handle == IntPtr.Zero)
                {
                    throw CreateWin32Exception("GetModuleHandle");
                }

                // The remote loader thread must start at LoadLibraryW.
                // codeql[cs/call-to-unmanaged-code]
                IntPtr loadLibraryAddress = GetProcAddress(kernel32Handle, "LoadLibraryW");
                if (loadLibraryAddress == IntPtr.Zero)
                {
                    throw CreateWin32Exception("GetProcAddress");
                }

                // Loading the monitor DLL requires running LoadLibraryW inside the target process.
                // codeql[cs/call-to-unmanaged-code]
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

                // The injector must wait on the remote loader thread before reading its module handle.
                // codeql[cs/call-to-unmanaged-code]
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

                // The LoadLibraryW exit code is the remote BO2Monitor module handle used to start the monitor.
                // codeql[cs/call-to-unmanaged-code]
                if (!GetExitCodeThread(threadHandle, out uint exitCode) || exitCode == 0)
                {
                    throw CreateWin32Exception("GetExitCodeThread");
                }

                // The loader thread handle can be closed once its module handle has been captured.
                // codeql[cs/call-to-unmanaged-code]
                CloseHandle(threadHandle);
                threadHandle = IntPtr.Zero;
                StartRemoteMonitor(processHandle, dllPath, new IntPtr(unchecked((int)exitCode)));
            }
            finally
            {
                if (threadHandle != IntPtr.Zero)
                {
                    // Cleanup must close the remote loader thread if startup exits early.
                    // codeql[cs/call-to-unmanaged-code]
                    CloseHandle(threadHandle);
                }

                if (remotePathAddress != IntPtr.Zero)
                {
                    // Cleanup must release the remote process buffer after the loader thread completes.
                    // codeql[cs/call-to-unmanaged-code]
                    VirtualFreeEx(processHandle, remotePathAddress, UIntPtr.Zero, MemRelease);
                }

                // Cleanup must release the remote process handle opened for injection.
                // codeql[cs/call-to-unmanaged-code]
                CloseHandle(processHandle);
            }
        }

        private static void StartRemoteMonitor(IntPtr processHandle, string dllPath, IntPtr remoteModuleHandle)
        {
            IntPtr startMonitorAddress = ResolveRemoteExportAddress(
                dllPath,
                remoteModuleHandle,
                StartMonitorExportName);
            // Starting the monitor requires running its validated StartMonitor export in the target process.
            // codeql[cs/call-to-unmanaged-code]
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
                // The injector must wait for StartMonitor to report whether it spawned the worker thread.
                // codeql[cs/call-to-unmanaged-code]
                uint waitResult = WaitForSingleObject(threadHandle, RemoteThreadTimeoutMilliseconds);
                if (waitResult != WaitObject0)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionStartFailedFormat", waitResult));
                }

                // The StartMonitor thread exit code is the payload's startup success flag.
                // codeql[cs/call-to-unmanaged-code]
                if (!GetExitCodeThread(threadHandle, out uint exitCode) || exitCode == 0)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionStartFailedFormat", exitCode));
                }
            }
            finally
            {
                // Cleanup must close the temporary StartMonitor thread handle.
                // codeql[cs/call-to-unmanaged-code]
                CloseHandle(threadHandle);
            }
        }

        private static IntPtr ResolveRemoteExportAddress(string dllPath, IntPtr remoteModuleHandle, string exportName)
        {
            try
            {
                uint exportRva = ResolvePeExportRva(dllPath, exportName);
                return new IntPtr(remoteModuleHandle.ToInt64() + exportRva);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
            {
                throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName), ex);
            }
        }

        internal static uint ResolvePeExportRva(string dllPath, string exportName)
        {
            using FileStream stream = File.OpenRead(dllPath);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);

            if (stream.Length < 0x40)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", dllPath));
            }

            if (reader.ReadUInt16() != 0x5A4D)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", dllPath));
            }

            stream.Position = 0x3C;
            int peHeaderOffset = reader.ReadInt32();
            if (peHeaderOffset <= 0 || peHeaderOffset > stream.Length - 6)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", dllPath));
            }

            stream.Position = peHeaderOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", dllPath));
            }

            stream.Position = peHeaderOffset + PeSignatureSize;
            if (reader.ReadUInt16() != ImageFileMachineI386)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidMachineFormat", dllPath));
            }

            ushort sectionCount = reader.ReadUInt16();
            stream.Position = peHeaderOffset + PeSignatureSize + 16;
            ushort optionalHeaderSize = reader.ReadUInt16();
            long optionalHeaderOffset = peHeaderOffset + PeSignatureSize + ImageFileHeaderSize;
            EnsureCanRead(stream, optionalHeaderOffset, optionalHeaderSize, dllPath);

            stream.Position = optionalHeaderOffset;
            if (reader.ReadUInt16() != Pe32Magic)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", dllPath));
            }

            long exportDirectoryEntryOffset = optionalHeaderOffset + Pe32ExportDirectoryOffset;
            EnsureCanRead(stream, exportDirectoryEntryOffset, ExportDirectoryEntrySize, dllPath);
            stream.Position = exportDirectoryEntryOffset;
            uint exportDirectoryRva = reader.ReadUInt32();
            uint exportDirectorySize = reader.ReadUInt32();
            if (exportDirectoryRva == 0 || exportDirectorySize == 0)
            {
                throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName));
            }

            PeSectionHeader[] sectionHeaders = ReadPeSectionHeaders(
                reader,
                stream,
                optionalHeaderOffset + optionalHeaderSize,
                sectionCount,
                dllPath);
            long exportDirectoryOffset = RvaToFileOffset(exportDirectoryRva, sectionHeaders, dllPath);
            EnsureCanRead(stream, exportDirectoryOffset, ExportDirectorySize, dllPath);

            stream.Position = exportDirectoryOffset + 20;
            uint numberOfFunctions = reader.ReadUInt32();
            uint numberOfNames = reader.ReadUInt32();
            uint addressOfFunctionsRva = reader.ReadUInt32();
            uint addressOfNamesRva = reader.ReadUInt32();
            uint addressOfNameOrdinalsRva = reader.ReadUInt32();
            long functionsOffset = RvaToFileOffset(addressOfFunctionsRva, sectionHeaders, dllPath);
            long namesOffset = RvaToFileOffset(addressOfNamesRva, sectionHeaders, dllPath);
            long ordinalsOffset = RvaToFileOffset(addressOfNameOrdinalsRva, sectionHeaders, dllPath);

            for (uint i = 0; i < numberOfNames; i++)
            {
                long nameRvaOffset = namesOffset + ((long)i * UInt32Size);
                EnsureCanRead(stream, nameRvaOffset, UInt32Size, dllPath);
                stream.Position = nameRvaOffset;
                uint nameRva = reader.ReadUInt32();
                string name = ReadAsciiNullTerminatedString(
                    reader,
                    stream,
                    RvaToFileOffset(nameRva, sectionHeaders, dllPath),
                    dllPath);
                if (!string.Equals(name, exportName, StringComparison.Ordinal))
                {
                    continue;
                }

                long ordinalOffset = ordinalsOffset + ((long)i * UInt16Size);
                EnsureCanRead(stream, ordinalOffset, UInt16Size, dllPath);
                stream.Position = ordinalOffset;
                ushort ordinal = reader.ReadUInt16();
                if (ordinal >= numberOfFunctions)
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName));
                }

                long functionRvaOffset = functionsOffset + ((long)ordinal * UInt32Size);
                EnsureCanRead(stream, functionRvaOffset, UInt32Size, dllPath);
                stream.Position = functionRvaOffset;
                uint functionRva = reader.ReadUInt32();
                if (functionRva == 0 || IsRvaInRange(functionRva, exportDirectoryRva, exportDirectorySize))
                {
                    throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName));
                }

                return functionRva;
            }

            throw new InvalidOperationException(AppStrings.Format("DllInjectionExportMissingFormat", exportName));
        }

        private static PeSectionHeader[] ReadPeSectionHeaders(
            BinaryReader reader,
            Stream stream,
            long sectionHeaderOffset,
            ushort sectionCount,
            string path)
        {
            EnsureCanRead(stream, sectionHeaderOffset, (long)sectionCount * SectionHeaderSize, path);
            PeSectionHeader[] sectionHeaders = new PeSectionHeader[sectionCount];
            for (int i = 0; i < sectionHeaders.Length; i++)
            {
                stream.Position = sectionHeaderOffset + ((long)i * SectionHeaderSize) + 8;
                uint virtualSize = reader.ReadUInt32();
                uint virtualAddress = reader.ReadUInt32();
                uint rawDataSize = reader.ReadUInt32();
                uint rawDataPointer = reader.ReadUInt32();
                sectionHeaders[i] = new PeSectionHeader(
                    virtualSize,
                    virtualAddress,
                    rawDataSize,
                    rawDataPointer);
            }

            return sectionHeaders;
        }

        private static long RvaToFileOffset(uint rva, PeSectionHeader[] sectionHeaders, string path)
        {
            foreach (PeSectionHeader sectionHeader in sectionHeaders)
            {
                uint sectionSize = sectionHeader.VirtualSize > sectionHeader.RawDataSize
                    ? sectionHeader.VirtualSize
                    : sectionHeader.RawDataSize;
                if (sectionSize == 0)
                {
                    continue;
                }

                ulong sectionStart = sectionHeader.VirtualAddress;
                ulong sectionEnd = sectionStart + sectionSize;
                if (rva >= sectionStart && (ulong)rva < sectionEnd)
                {
                    return sectionHeader.RawDataPointer + ((long)rva - sectionHeader.VirtualAddress);
                }
            }

            throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
        }

        private static string ReadAsciiNullTerminatedString(
            BinaryReader reader,
            Stream stream,
            long offset,
            string path)
        {
            EnsureCanRead(stream, offset, 1, path);
            stream.Position = offset;
            StringBuilder builder = new();
            while (stream.Position < stream.Length)
            {
                byte value = reader.ReadByte();
                if (value == 0)
                {
                    return builder.ToString();
                }

                builder.Append((char)value);
            }

            throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
        }

        private static void EnsureCanRead(Stream stream, long offset, long length, string path)
        {
            if (offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset)
            {
                throw new BadImageFormatException(AppStrings.Format("DllInjectionInvalidDllFormat", path));
            }
        }

        private static bool IsRvaInRange(uint rva, uint start, uint size)
        {
            return rva >= start && (ulong)rva < (ulong)start + size;
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

        private readonly record struct PeSectionHeader(
            uint VirtualSize,
            uint VirtualAddress,
            uint RawDataSize,
            uint RawDataPointer);

        // Native interop for injection is intentionally centralized here. Each declaration
        // binds kernel32.dll from System32; payload paths are validated before use.

        // Required to open the detected game process with the minimal access mask used by injection.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        // Required to allocate a transient remote buffer for the validated monitor path.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr VirtualAllocEx(
            IntPtr processHandle,
            IntPtr address,
            UIntPtr size,
            uint allocationType,
            uint protect);

        // Required to release the remote buffer after the loader thread finishes.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool VirtualFreeEx(
            IntPtr processHandle,
            IntPtr address,
            UIntPtr size,
            uint freeType);

        // Required to copy the validated monitor path into the detected game process.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool WriteProcessMemory(
            IntPtr processHandle,
            IntPtr baseAddress,
            byte[] buffer,
            int size,
            out UIntPtr numberOfBytesWritten);

        // Required to resolve the local kernel32 LoadLibraryW address used as the remote thread entry point.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        // Required to resolve LoadLibraryW; StartMonitor export lookup is parsed from PE metadata.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetProcAddress(
            IntPtr moduleHandle,
            [MarshalAs(UnmanagedType.LPStr)] string procName);

        // Required to run LoadLibraryW and StartMonitor inside the detected game process.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateRemoteThread(
            IntPtr processHandle,
            IntPtr threadAttributes,
            uint stackSize,
            IntPtr startAddress,
            IntPtr parameter,
            uint creationFlags,
            IntPtr threadId);

        // Required to bound waits for remote loader/startup threads.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        // Required to verify that the remote loader/startup thread succeeded.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool GetExitCodeThread(IntPtr threadHandle, out uint exitCode);

        // Required to release native process and thread handles acquired during injection.
        // codeql[cs/unmanaged-code]
        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
