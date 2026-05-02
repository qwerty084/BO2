using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    internal sealed class WindowsProcessMemoryAccessor : IProcessMemoryAccessor
    {
        private const int Int32Size = sizeof(int);
        private const int SingleSize = sizeof(float);
        private static readonly IntPtr InvalidHandleValue = new(-1);

        private SafeProcessHandle? _processHandle;
        private int? _attachedProcessId;

        public void Attach(int processId, string processName)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);

            if (string.IsNullOrWhiteSpace(processName))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(processName));
            }

            if (_attachedProcessId == processId && _processHandle is not null && !_processHandle.IsInvalid && !_processHandle.IsClosed)
            {
                return;
            }

            Close();
            _processHandle = OpenProcess(ProcessAccess.QueryLimitedInformation | ProcessAccess.VirtualMemoryRead, false, processId);

            if (_processHandle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                Close();
                throw new Win32Exception(error, AppStrings.Format("OpenProcessFailedFormat", processName));
            }

            _attachedProcessId = processId;
        }

        public int ReadInt32(uint address, string valueName)
        {
            SafeProcessHandle processHandle = GetRequiredProcessHandle();
            byte[] buffer = new byte[Int32Size];
            nuint size = Int32Size;

            if (!ReadProcessMemory(processHandle, new IntPtr(unchecked((long)address)), buffer, size, out nuint bytesRead))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), AppStrings.Format("ReadMemoryFailedFormat", valueName));
            }

            if (bytesRead != size)
            {
                throw new InvalidOperationException(AppStrings.Format("ShortReadFormat", size, bytesRead));
            }

            return BitConverter.ToInt32(buffer, 0);
        }

        public float ReadSingle(uint address, string valueName)
        {
            SafeProcessHandle processHandle = GetRequiredProcessHandle();
            byte[] buffer = new byte[SingleSize];
            nuint size = SingleSize;

            if (!ReadProcessMemory(processHandle, new IntPtr(unchecked((long)address)), buffer, size, out nuint bytesRead))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), AppStrings.Format("ReadMemoryFailedFormat", valueName));
            }

            if (bytesRead != size)
            {
                throw new InvalidOperationException(AppStrings.Format("ShortReadFormat", size, bytesRead));
            }

            return BitConverter.ToSingle(buffer, 0);
        }

        public void Close()
        {
            _processHandle?.Dispose();
            _processHandle = null;
            _attachedProcessId = null;
        }

        public void Dispose()
        {
            Close();
        }

        private SafeProcessHandle GetRequiredProcessHandle()
        {
            if (_processHandle is null || _processHandle.IsInvalid || _processHandle.IsClosed)
            {
                throw new InvalidOperationException(AppStrings.Get("GameProcessHandleUnavailable"));
            }

            return _processHandle;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(ProcessAccess desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            SafeProcessHandle process,
            nint baseAddress,
            byte[] buffer,
            nuint size,
            out nuint numberOfBytesRead);

        [Flags]
        private enum ProcessAccess
        {
            QueryLimitedInformation = 0x1000,
            VirtualMemoryRead = 0x0010
        }

        private sealed class SafeProcessHandle : SafeHandle
        {
            public SafeProcessHandle()
                : base(InvalidHandleValue, true)
            {
            }

            public override bool IsInvalid => handle == IntPtr.Zero || handle == InvalidHandleValue;

            protected override bool ReleaseHandle()
            {
                return CloseHandle(handle);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
