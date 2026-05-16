#include "InjectorOrchestration.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

namespace BO2InjectorHelper
{
    namespace
    {
        constexpr wchar_t MonitorPayloadFileName[] = L"BO2Monitor.dll";
        constexpr wchar_t StartMonitorExportName[] = L"StartMonitor";
        constexpr char StartMonitorExportNameAscii[] = "StartMonitor";
        constexpr DWORD MaxExecutablePathCharacters = 32768;

        void PrintLastError(IInjectorWindowsApi& api, const wchar_t* apiName)
        {
            std::wcerr << apiName << L" failed with error " << api.GetLastError() << L'\n';
        }

        bool EqualOrdinalIgnoreCase(const std::wstring& left, const std::wstring& right)
        {
            return CompareStringOrdinal(left.c_str(), -1, right.c_str(), -1, TRUE) == CSTR_EQUAL;
        }

        bool PathIsUnder(
            const std::filesystem::path& childPath,
            const std::filesystem::path& parentPath)
        {
            auto child = childPath.begin();
            auto parent = parentPath.begin();
            for (; parent != parentPath.end(); ++parent, ++child)
            {
                if (child == childPath.end() || !EqualOrdinalIgnoreCase(child->wstring(), parent->wstring()))
                {
                    return false;
                }
            }

            return true;
        }

        bool TryReadFile(const std::filesystem::path& path, std::vector<std::uint8_t>& bytes)
        {
            bytes.clear();
            std::ifstream stream(path, std::ios::binary);
            if (!stream)
            {
                return false;
            }

            stream.seekg(0, std::ios::end);
            const std::streamoff length = stream.tellg();
            if (length < 0)
            {
                return false;
            }

            bytes.resize(static_cast<std::size_t>(length));
            stream.seekg(0, std::ios::beg);
            if (bytes.empty())
            {
                return true;
            }

            stream.read(reinterpret_cast<char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
            return stream.gcount() == static_cast<std::streamsize>(bytes.size());
        }

        bool CanRead(const std::vector<std::uint8_t>& bytes, std::size_t offset, std::size_t length)
        {
            return offset <= bytes.size() && length <= bytes.size() - offset;
        }

        template<typename T>
        bool TryReadStruct(const std::vector<std::uint8_t>& bytes, std::size_t offset, T& value)
        {
            if (!CanRead(bytes, offset, sizeof(T)))
            {
                return false;
            }

            std::memcpy(&value, bytes.data() + offset, sizeof(T));
            return true;
        }

        bool TryReadUInt32(const std::vector<std::uint8_t>& bytes, std::size_t offset, std::uint32_t& value)
        {
            return TryReadStruct(bytes, offset, value);
        }

        bool TryReadUInt16(const std::vector<std::uint8_t>& bytes, std::size_t offset, std::uint16_t& value)
        {
            return TryReadStruct(bytes, offset, value);
        }

        bool TryReadNullTerminatedAscii(
            const std::vector<std::uint8_t>& bytes,
            std::size_t offset,
            std::string& value)
        {
            if (offset >= bytes.size())
            {
                return false;
            }

            value.clear();
            for (std::size_t index = offset; index < bytes.size(); ++index)
            {
                const std::uint8_t character = bytes[index];
                if (character == 0)
                {
                    return true;
                }

                value.push_back(static_cast<char>(character));
            }

            return false;
        }

        bool TryRvaToFileOffset(
            const std::vector<std::uint8_t>& bytes,
            const IMAGE_NT_HEADERS32& ntHeaders,
            const std::vector<IMAGE_SECTION_HEADER>& sectionHeaders,
            std::uint32_t rva,
            std::size_t& fileOffset)
        {
            if (rva < ntHeaders.OptionalHeader.SizeOfHeaders && rva < bytes.size())
            {
                fileOffset = rva;
                return true;
            }

            for (const IMAGE_SECTION_HEADER& sectionHeader : sectionHeaders)
            {
                const std::uint32_t sectionSize = std::max(
                    sectionHeader.Misc.VirtualSize,
                    sectionHeader.SizeOfRawData);
                if (sectionSize == 0)
                {
                    continue;
                }

                const std::uint64_t sectionStart = sectionHeader.VirtualAddress;
                const std::uint64_t sectionEnd = sectionStart + sectionSize;
                if (rva < sectionStart || static_cast<std::uint64_t>(rva) >= sectionEnd)
                {
                    continue;
                }

                const std::uint64_t offsetInSection = static_cast<std::uint64_t>(rva) - sectionStart;
                if (offsetInSection >= sectionHeader.SizeOfRawData)
                {
                    return false;
                }

                const std::uint64_t candidateOffset = sectionHeader.PointerToRawData + offsetInSection;
                if (candidateOffset > bytes.size())
                {
                    return false;
                }

                fileOffset = static_cast<std::size_t>(candidateOffset);
                return true;
            }

            return false;
        }

        bool IsRvaInRange(std::uint32_t rva, std::uint32_t start, std::uint32_t size)
        {
            return rva >= start && static_cast<std::uint64_t>(rva) < static_cast<std::uint64_t>(start) + size;
        }

        PayloadValidationStatus ValidatePePayload(const std::filesystem::path& dllPath)
        {
            std::vector<std::uint8_t> bytes;
            if (!TryReadFile(dllPath, bytes))
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            IMAGE_DOS_HEADER dosHeader{};
            if (!TryReadStruct(bytes, 0, dosHeader) || dosHeader.e_magic != IMAGE_DOS_SIGNATURE)
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            if (dosHeader.e_lfanew <= 0)
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            const std::size_t ntHeaderOffset = static_cast<std::size_t>(dosHeader.e_lfanew);
            IMAGE_NT_HEADERS32 ntHeaders{};
            if (!TryReadStruct(bytes, ntHeaderOffset, ntHeaders) || ntHeaders.Signature != IMAGE_NT_SIGNATURE)
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            if (ntHeaders.FileHeader.Machine != IMAGE_FILE_MACHINE_I386)
            {
                return PayloadValidationStatus::InvalidPeMachine;
            }

            if ((ntHeaders.FileHeader.Characteristics & IMAGE_FILE_DLL) == 0)
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            if (ntHeaders.FileHeader.SizeOfOptionalHeader < sizeof(IMAGE_OPTIONAL_HEADER32)
                || ntHeaders.OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC)
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            if (ntHeaders.OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_EXPORT)
            {
                return PayloadValidationStatus::MissingStartMonitorExport;
            }

            const IMAGE_DATA_DIRECTORY& exportDirectoryEntry =
                ntHeaders.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXPORT];
            if (exportDirectoryEntry.VirtualAddress == 0 || exportDirectoryEntry.Size == 0)
            {
                return PayloadValidationStatus::MissingStartMonitorExport;
            }

            const std::size_t sectionHeadersOffset =
                ntHeaderOffset
                + offsetof(IMAGE_NT_HEADERS32, OptionalHeader)
                + ntHeaders.FileHeader.SizeOfOptionalHeader;
            const std::size_t sectionHeadersSize =
                static_cast<std::size_t>(ntHeaders.FileHeader.NumberOfSections) * sizeof(IMAGE_SECTION_HEADER);
            if (!CanRead(bytes, sectionHeadersOffset, sectionHeadersSize))
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            std::vector<IMAGE_SECTION_HEADER> sectionHeaders(ntHeaders.FileHeader.NumberOfSections);
            for (std::size_t index = 0; index < sectionHeaders.size(); ++index)
            {
                if (!TryReadStruct(
                    bytes,
                    sectionHeadersOffset + (index * sizeof(IMAGE_SECTION_HEADER)),
                    sectionHeaders[index]))
                {
                    return PayloadValidationStatus::InvalidPeFormat;
                }
            }

            std::size_t exportDirectoryOffset = 0;
            if (!TryRvaToFileOffset(
                bytes,
                ntHeaders,
                sectionHeaders,
                exportDirectoryEntry.VirtualAddress,
                exportDirectoryOffset))
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            IMAGE_EXPORT_DIRECTORY exportDirectory{};
            if (!TryReadStruct(bytes, exportDirectoryOffset, exportDirectory))
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            if (exportDirectory.NumberOfNames == 0 || exportDirectory.NumberOfFunctions == 0)
            {
                return PayloadValidationStatus::MissingStartMonitorExport;
            }

            std::size_t functionTableOffset = 0;
            std::size_t nameTableOffset = 0;
            std::size_t ordinalTableOffset = 0;
            if (!TryRvaToFileOffset(bytes, ntHeaders, sectionHeaders, exportDirectory.AddressOfFunctions, functionTableOffset)
                || !TryRvaToFileOffset(bytes, ntHeaders, sectionHeaders, exportDirectory.AddressOfNames, nameTableOffset)
                || !TryRvaToFileOffset(bytes, ntHeaders, sectionHeaders, exportDirectory.AddressOfNameOrdinals, ordinalTableOffset))
            {
                return PayloadValidationStatus::InvalidPeFormat;
            }

            for (std::uint32_t index = 0; index < exportDirectory.NumberOfNames; ++index)
            {
                std::uint32_t nameRva = 0;
                if (!TryReadUInt32(
                    bytes,
                    nameTableOffset + (static_cast<std::size_t>(index) * sizeof(std::uint32_t)),
                    nameRva))
                {
                    return PayloadValidationStatus::InvalidPeFormat;
                }

                std::size_t nameOffset = 0;
                std::string exportName;
                if (!TryRvaToFileOffset(bytes, ntHeaders, sectionHeaders, nameRva, nameOffset)
                    || !TryReadNullTerminatedAscii(bytes, nameOffset, exportName))
                {
                    return PayloadValidationStatus::InvalidPeFormat;
                }

                if (exportName != StartMonitorExportNameAscii)
                {
                    continue;
                }

                std::uint16_t ordinal = 0;
                if (!TryReadUInt16(
                    bytes,
                    ordinalTableOffset + (static_cast<std::size_t>(index) * sizeof(std::uint16_t)),
                    ordinal))
                {
                    return PayloadValidationStatus::InvalidPeFormat;
                }

                if (ordinal >= exportDirectory.NumberOfFunctions)
                {
                    return PayloadValidationStatus::MissingStartMonitorExport;
                }

                std::uint32_t functionRva = 0;
                if (!TryReadUInt32(
                    bytes,
                    functionTableOffset + (static_cast<std::size_t>(ordinal) * sizeof(std::uint32_t)),
                    functionRva))
                {
                    return PayloadValidationStatus::InvalidPeFormat;
                }

                if (functionRva == 0
                    || IsRvaInRange(functionRva, exportDirectoryEntry.VirtualAddress, exportDirectoryEntry.Size))
                {
                    return PayloadValidationStatus::MissingStartMonitorExport;
                }

                return PayloadValidationStatus::Success;
            }

            return PayloadValidationStatus::MissingStartMonitorExport;
        }
    }

    HANDLE Win32InjectorWindowsApi::OpenProcess(DWORD desiredAccess, BOOL inheritHandle, DWORD processId)
    {
        return ::OpenProcess(desiredAccess, inheritHandle, processId);
    }

    LPVOID Win32InjectorWindowsApi::VirtualAllocEx(
        HANDLE processHandle,
        LPVOID address,
        SIZE_T size,
        DWORD allocationType,
        DWORD protect)
    {
        return ::VirtualAllocEx(processHandle, address, size, allocationType, protect);
    }

    BOOL Win32InjectorWindowsApi::WriteProcessMemory(
        HANDLE processHandle,
        LPVOID baseAddress,
        LPCVOID buffer,
        SIZE_T size,
        SIZE_T* bytesWritten)
    {
        return ::WriteProcessMemory(processHandle, baseAddress, buffer, size, bytesWritten);
    }

    HMODULE Win32InjectorWindowsApi::GetModuleHandleW(LPCWSTR moduleName)
    {
        return ::GetModuleHandleW(moduleName);
    }

    FARPROC Win32InjectorWindowsApi::GetProcAddress(HMODULE moduleHandle, LPCSTR procedureName)
    {
        return ::GetProcAddress(moduleHandle, procedureName);
    }

    HANDLE Win32InjectorWindowsApi::CreateRemoteThread(
        HANDLE processHandle,
        LPSECURITY_ATTRIBUTES threadAttributes,
        SIZE_T stackSize,
        LPTHREAD_START_ROUTINE startAddress,
        LPVOID parameter,
        DWORD creationFlags,
        LPDWORD threadId)
    {
        return ::CreateRemoteThread(
            processHandle,
            threadAttributes,
            stackSize,
            startAddress,
            parameter,
            creationFlags,
            threadId);
    }

    DWORD Win32InjectorWindowsApi::WaitForSingleObject(HANDLE handle, DWORD milliseconds)
    {
        return ::WaitForSingleObject(handle, milliseconds);
    }

    BOOL Win32InjectorWindowsApi::GetExitCodeThread(HANDLE threadHandle, LPDWORD exitCode)
    {
        return ::GetExitCodeThread(threadHandle, exitCode);
    }

    BOOL Win32InjectorWindowsApi::VirtualFreeEx(HANDLE processHandle, LPVOID address, SIZE_T size, DWORD freeType)
    {
        return ::VirtualFreeEx(processHandle, address, size, freeType);
    }

    BOOL Win32InjectorWindowsApi::CloseHandle(HANDLE handle)
    {
        return ::CloseHandle(handle);
    }

    HMODULE Win32InjectorWindowsApi::LoadLibraryExW(LPCWSTR fileName, HANDLE fileHandle, DWORD flags)
    {
        return ::LoadLibraryExW(fileName, fileHandle, flags);
    }

    BOOL Win32InjectorWindowsApi::FreeLibrary(HMODULE moduleHandle)
    {
        return ::FreeLibrary(moduleHandle);
    }

    DWORD Win32InjectorWindowsApi::GetLastError()
    {
        return ::GetLastError();
    }

    void* CalculateRemoteExportAddress(
        DWORD_PTR localModuleBase,
        DWORD_PTR localExportAddress,
        DWORD_PTR remoteModuleBase) noexcept
    {
        if (localModuleBase == 0 || localExportAddress < localModuleBase || remoteModuleBase == 0)
        {
            return nullptr;
        }

        const DWORD_PTR exportOffset = localExportAddress - localModuleBase;
        return reinterpret_cast<void*>(remoteModuleBase + exportOffset);
    }

    bool ResolveRemoteExportAddress(
        IInjectorWindowsApi& api,
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const char* exportName,
        void*& remoteExportAddress)
    {
        remoteExportAddress = nullptr;
        HMODULE localModule = api.LoadLibraryExW(dllPath.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
        if (localModule == nullptr)
        {
            PrintLastError(api, L"LoadLibraryExW");
            return false;
        }

        FARPROC localExport = api.GetProcAddress(localModule, exportName);
        if (localExport == nullptr)
        {
            std::wcerr << StartMonitorExportName << L" export not found\n";
            api.FreeLibrary(localModule);
            return false;
        }

        remoteExportAddress = CalculateRemoteExportAddress(
            reinterpret_cast<DWORD_PTR>(localModule),
            reinterpret_cast<DWORD_PTR>(localExport),
            remoteModuleBase);
        api.FreeLibrary(localModule);
        return remoteExportAddress != nullptr;
    }

    bool StartRemoteMonitor(
        IInjectorWindowsApi& api,
        HANDLE processHandle,
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const InjectorOrchestrationOptions& options)
    {
        void* startMonitorAddress = nullptr;
        if (!ResolveRemoteExportAddress(api, dllPath, remoteModuleBase, "StartMonitor", startMonitorAddress))
        {
            return false;
        }

        HANDLE threadHandle = api.CreateRemoteThread(
            processHandle,
            nullptr,
            0,
            reinterpret_cast<LPTHREAD_START_ROUTINE>(startMonitorAddress),
            nullptr,
            0,
            nullptr);
        if (threadHandle == nullptr)
        {
            PrintLastError(api, L"CreateRemoteThread");
            return false;
        }

        const DWORD waitResult = api.WaitForSingleObject(threadHandle, options.WaitMilliseconds);
        DWORD exitCode = 0;
        const bool succeeded = waitResult == WAIT_OBJECT_0
            && api.GetExitCodeThread(threadHandle, &exitCode)
            && exitCode != 0;
        if (!succeeded)
        {
            std::wcerr << L"StartMonitor failed with result " << waitResult
                << L" and exit code " << exitCode << L'\n';
        }

        api.CloseHandle(threadHandle);
        return succeeded;
    }

    std::wstring GetCurrentExecutablePath()
    {
        DWORD bufferSize = MAX_PATH;
        while (bufferSize <= MaxExecutablePathCharacters)
        {
            std::wstring buffer(bufferSize, L'\0');
            const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), bufferSize);
            if (length == 0)
            {
                return std::wstring{};
            }

            if (length < bufferSize)
            {
                buffer.resize(length);
                return buffer;
            }

            bufferSize *= 2;
        }

        return std::wstring{};
    }

    PayloadValidationStatus ValidateMonitorPayload(
        const std::wstring& dllPath,
        const std::wstring& helperExecutablePath,
        std::wstring& canonicalDllPath)
    {
        canonicalDllPath.clear();
        std::error_code error;
        const std::filesystem::path canonicalDllPathCandidate =
            std::filesystem::canonical(dllPath, error);
        if (error
            || !std::filesystem::is_regular_file(canonicalDllPathCandidate, error)
            || error)
        {
            return PayloadValidationStatus::InvalidPath;
        }

        const std::filesystem::path canonicalHelperPath =
            std::filesystem::canonical(helperExecutablePath, error);
        if (error || canonicalHelperPath.empty())
        {
            return PayloadValidationStatus::InvalidPath;
        }

        const std::filesystem::path helperDirectory = canonicalHelperPath.parent_path();
        if (helperDirectory.empty() || !PathIsUnder(canonicalDllPathCandidate, helperDirectory))
        {
            return PayloadValidationStatus::InvalidPath;
        }

        if (!EqualOrdinalIgnoreCase(canonicalDllPathCandidate.filename().wstring(), MonitorPayloadFileName))
        {
            return PayloadValidationStatus::InvalidFileName;
        }

        const PayloadValidationStatus peStatus = ValidatePePayload(canonicalDllPathCandidate);
        if (peStatus != PayloadValidationStatus::Success)
        {
            return peStatus;
        }

        canonicalDllPath = canonicalDllPathCandidate.wstring();
        return PayloadValidationStatus::Success;
    }

    bool InjectLibrary(
        IInjectorWindowsApi& api,
        DWORD processId,
        const std::wstring& dllPath,
        const InjectorOrchestrationOptions& options)
    {
        const std::size_t byteLength = (dllPath.size() + 1) * sizeof(wchar_t);
        HANDLE processHandle = api.OpenProcess(InjectionProcessAccess, FALSE, processId);
        if (processHandle == nullptr)
        {
            PrintLastError(api, L"OpenProcess");
            return false;
        }

        void* remotePath = nullptr;
        HANDLE threadHandle = nullptr;
        SIZE_T bytesWritten = 0;
        HMODULE kernel32Handle = nullptr;
        LPTHREAD_START_ROUTINE loadLibraryAddress = nullptr;
        DWORD waitResult = 0;
        DWORD exitCode = 0;
        bool succeeded = false;

        remotePath = api.VirtualAllocEx(
            processHandle,
            nullptr,
            byteLength,
            MEM_COMMIT | MEM_RESERVE,
            PAGE_READWRITE);
        if (remotePath == nullptr)
        {
            PrintLastError(api, L"VirtualAllocEx");
            goto Cleanup;
        }

        if (!api.WriteProcessMemory(
            processHandle,
            remotePath,
            dllPath.c_str(),
            byteLength,
            &bytesWritten)
            || bytesWritten != byteLength)
        {
            PrintLastError(api, L"WriteProcessMemory");
            goto Cleanup;
        }

        kernel32Handle = api.GetModuleHandleW(L"kernel32.dll");
        if (kernel32Handle == nullptr)
        {
            PrintLastError(api, L"GetModuleHandleW");
            goto Cleanup;
        }

        loadLibraryAddress = reinterpret_cast<LPTHREAD_START_ROUTINE>(
            api.GetProcAddress(kernel32Handle, "LoadLibraryW"));
        if (loadLibraryAddress == nullptr)
        {
            PrintLastError(api, L"GetProcAddress");
            goto Cleanup;
        }

        threadHandle = api.CreateRemoteThread(
            processHandle,
            nullptr,
            0,
            loadLibraryAddress,
            remotePath,
            0,
            nullptr);
        if (threadHandle == nullptr)
        {
            PrintLastError(api, L"CreateRemoteThread");
            goto Cleanup;
        }

        waitResult = api.WaitForSingleObject(threadHandle, options.WaitMilliseconds);
        if (waitResult != WAIT_OBJECT_0)
        {
            std::wcerr << L"Remote loader wait failed with result " << waitResult << L'\n';
            // The remote loader thread may still be reading this buffer after our wait expires.
            // Once the thread has started, only free the path after definitive completion.
            remotePath = nullptr;
            goto Cleanup;
        }

        if (!api.GetExitCodeThread(threadHandle, &exitCode) || exitCode == 0)
        {
            PrintLastError(api, L"GetExitCodeThread");
            goto Cleanup;
        }

        std::wcout << L"LoadLibrary exit=0x" << std::hex << exitCode << L'\n';
        succeeded = StartRemoteMonitor(api, processHandle, dllPath, static_cast<DWORD_PTR>(exitCode), options);

Cleanup:
        if (threadHandle != nullptr)
        {
            api.CloseHandle(threadHandle);
        }

        if (remotePath != nullptr)
        {
            api.VirtualFreeEx(processHandle, remotePath, 0, MEM_RELEASE);
        }

        api.CloseHandle(processHandle);
        return succeeded;
    }
}
