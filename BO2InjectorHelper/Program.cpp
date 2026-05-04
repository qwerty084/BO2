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

    const std::wstring& dllPath = arguments.DllPath;
    if (GetFileAttributesW(dllPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        PrintLastError(L"GetFileAttributesW");
        return 2;
    }

    BO2InjectorHelper::Win32InjectorWindowsApi api;
    return BO2InjectorHelper::InjectLibrary(api, arguments.ProcessId, dllPath) ? 0 : 1;
}
