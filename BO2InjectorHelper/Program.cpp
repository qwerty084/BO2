#include "InjectorArguments.h"
#include "InjectorOrchestration.h"

#include <Windows.h>

#include <iostream>
#include <string>

namespace
{
    void PrintLastError(const wchar_t* apiName)
    {
        std::wcerr << apiName << L" failed with error " << GetLastError() << L'\n';
    }

    const wchar_t* PayloadValidationMessage(BO2InjectorHelper::PayloadValidationStatus status)
    {
        switch (status)
        {
        case BO2InjectorHelper::PayloadValidationStatus::InvalidPath:
            return L"Invalid monitor DLL path";
        case BO2InjectorHelper::PayloadValidationStatus::InvalidFileName:
            return L"Monitor payload must be BO2Monitor.dll";
        case BO2InjectorHelper::PayloadValidationStatus::InvalidPeFormat:
            return L"Monitor payload is not a valid PE file";
        case BO2InjectorHelper::PayloadValidationStatus::InvalidPeMachine:
            return L"Monitor payload must be a 32-bit x86 PE";
        case BO2InjectorHelper::PayloadValidationStatus::MissingStartMonitorExport:
            return L"Monitor payload is missing StartMonitor export";
        case BO2InjectorHelper::PayloadValidationStatus::Success:
        default:
            return L"Monitor payload validation failed";
        }
    }
}

int wmain(int argc, wchar_t* argv[])
{
    BO2InjectorHelper::InjectorArguments arguments{};
    const BO2InjectorHelper::ParseArgumentsStatus parseStatus =
        BO2InjectorHelper::ParseInjectorArguments(argc, argv, arguments);
    if (parseStatus == BO2InjectorHelper::ParseArgumentsStatus::Usage)
    {
        std::wcerr << L"Usage: BO2InjectorHelper.exe <process-id> <dll-path>\n";
        return 2;
    }

    if (parseStatus == BO2InjectorHelper::ParseArgumentsStatus::InvalidProcessId)
    {
        std::wcerr << L"Invalid process id\n";
        return 2;
    }

    const std::wstring helperExecutablePath = BO2InjectorHelper::GetCurrentExecutablePath();
    if (helperExecutablePath.empty())
    {
        PrintLastError(L"GetModuleFileNameW");
        return 2;
    }

    std::wstring dllPath;
    const BO2InjectorHelper::PayloadValidationStatus payloadStatus =
        BO2InjectorHelper::ValidateMonitorPayload(arguments.DllPath, helperExecutablePath, dllPath);
    if (payloadStatus != BO2InjectorHelper::PayloadValidationStatus::Success)
    {
        std::wcerr << PayloadValidationMessage(payloadStatus) << L'\n';
        return 2;
    }

    BO2InjectorHelper::Win32InjectorWindowsApi api;
    return BO2InjectorHelper::InjectLibrary(api, arguments.ProcessId, dllPath) ? 0 : 1;
}
