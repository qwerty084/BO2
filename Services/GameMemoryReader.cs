using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    public sealed class GameMemoryReader : IDisposable
    {
        private const int Int32Size = sizeof(int);

        private static readonly IntPtr InvalidHandleValue = new(-1);

        private readonly GameProcessDetector _processDetector = new();
        private SafeProcessHandle? _processHandle;
        private int? _attachedProcessId;

        public PlayerStatsReadResult ReadPlayerStats()
        {
            DetectedGame? detectedGame = _processDetector.Detect();

            if (detectedGame is null)
            {
                CloseAttachedProcess();
                return PlayerStatsReadResult.GameNotRunning;
            }

            if (detectedGame.AddressMap is null)
            {
                CloseAttachedProcess();
                string statusText = string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                    ? AppStrings.Format("UnsupportedStatusFormat", detectedGame.DisplayName)
                    : AppStrings.Format("UnsupportedStatusWithReasonFormat", detectedGame.DisplayName, detectedGame.UnsupportedReason);

                return new PlayerStatsReadResult(
                    detectedGame,
                    null,
                    statusText,
                    ConnectionState.Unsupported);
            }

            EnsureAttached(detectedGame);

            try
            {
                PlayerStatAddressMap addressMap = detectedGame.AddressMap;
                PlayerStats stats = new(
                    ReadInt32(addressMap.PointsAddress, "points"),
                    ReadInt32(addressMap.KillsAddress, "kills"),
                    ReadInt32(addressMap.DownsAddress, "downs"),
                    ReadInt32(addressMap.RevivesAddress, "revives"),
                    ReadInt32(addressMap.HeadshotsAddress, "headshots"));

                return new PlayerStatsReadResult(
                    detectedGame,
                    stats,
                    AppStrings.Format("ConnectedStatusFormat", detectedGame.DisplayName),
                    ConnectionState.Connected);
            }
            catch
            {
                CloseAttachedProcess();
                throw;
            }
        }

        public void Dispose()
        {
            CloseAttachedProcess();
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

        private void EnsureAttached(DetectedGame detectedGame)
        {
            if (_attachedProcessId == detectedGame.ProcessId && _processHandle is not null && !_processHandle.IsInvalid && !_processHandle.IsClosed)
            {
                return;
            }

            CloseAttachedProcess();
            _processHandle = OpenProcess(ProcessAccess.QueryLimitedInformation | ProcessAccess.VirtualMemoryRead, false, detectedGame.ProcessId);

            if (_processHandle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                CloseAttachedProcess();
                throw new Win32Exception(error, $"Unable to open {detectedGame.ProcessName}.exe for read-only memory access.");
            }

            _attachedProcessId = detectedGame.ProcessId;
        }

        private void CloseAttachedProcess()
        {
            _processHandle?.Dispose();
            _processHandle = null;
            _attachedProcessId = null;
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
