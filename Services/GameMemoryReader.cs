using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    public sealed class GameMemoryReader : IDisposable
    {
        private const string TargetProcessName = "t6zm";
        private const int PlayerPointsAddress = 0x0234C068;
        private const int PlayerKillsAddress = 0x0234C080;
        private const int PlayerDownsAddress = 0x0234C084;
        private const int PlayerRevivesAddress = 0x0234C088;
        private const int PlayerHeadshotsAddress = 0x0234C08C;
        private const int Int32Size = sizeof(int);

        private static readonly IntPtr InvalidHandleValue = new(-1);

        private SafeProcessHandle? _processHandle;
        private int? _attachedProcessId;

        public PlayerStats ReadPlayerStats()
        {
            EnsureAttached();

            return new PlayerStats(
                ReadInt32(PlayerPointsAddress, "points"),
                ReadInt32(PlayerKillsAddress, "kills"),
                ReadInt32(PlayerDownsAddress, "downs"),
                ReadInt32(PlayerRevivesAddress, "revives"),
                ReadInt32(PlayerHeadshotsAddress, "headshots"));
        }

        public int ReadPlayerPoints()
        {
            EnsureAttached();

            return ReadInt32(PlayerPointsAddress, "player points");
        }

        public void Dispose()
        {
            _processHandle?.Dispose();
        }

        private int ReadInt32(int address, string valueName)
        {
            if (_processHandle is null || _processHandle.IsInvalid || _processHandle.IsClosed)
            {
                throw new InvalidOperationException("The game process handle is not available.");
            }

            byte[] buffer = new byte[Int32Size];
            if (!ReadProcessMemory(_processHandle, new IntPtr(address), buffer, Int32Size, out int bytesRead))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to read {valueName} from game memory.");
            }

            if (bytesRead != Int32Size)
            {
                throw new InvalidOperationException($"Expected to read {Int32Size} bytes but read {bytesRead} bytes.");
            }

            return BitConverter.ToInt32(buffer, 0);
        }

        private void EnsureAttached()
        {
            using Process process = GetTargetProcess();

            if (_attachedProcessId == process.Id && _processHandle is not null && !_processHandle.IsInvalid && !_processHandle.IsClosed)
            {
                return;
            }

            _processHandle?.Dispose();
            _processHandle = OpenProcess(ProcessAccess.QueryLimitedInformation | ProcessAccess.VirtualMemoryRead, false, process.Id);

            if (_processHandle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to open {TargetProcessName}.exe for read-only memory access.");
            }

            _attachedProcessId = process.Id;
        }

        private static Process GetTargetProcess()
        {
            Process[] processes = Process.GetProcessesByName(TargetProcessName);

            if (processes.Length == 0)
            {
                throw new InvalidOperationException($"{TargetProcessName}.exe is not running.");
            }

            for (int index = 1; index < processes.Length; index++)
            {
                processes[index].Dispose();
            }

            return processes[0];
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(ProcessAccess desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(
            SafeProcessHandle process,
            IntPtr baseAddress,
            byte[] buffer,
            int size,
            out int numberOfBytesRead);

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
