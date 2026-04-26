param(
    [int]$ProcessId,
    [string]$DllPath
)

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

    public static uint Inject(int processId, string dllPath)
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

            WaitForSingleObject(threadHandle, INFINITE);
            uint exitCode;
            if (!GetExitCodeThread(threadHandle, out exitCode))
            {
                ThrowLastError("GetExitCodeThread");
            }

            return exitCode;
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

if (-not (Test-Path -LiteralPath $DllPath)) {
    throw "DLL not found: $DllPath"
}

Add-Type -TypeDefinition $source
$exitCode = [RemoteDllInjector]::Inject($ProcessId, $DllPath)
"LoadLibrary exit=0x{0:X8}" -f $exitCode
