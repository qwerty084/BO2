#pragma once

#include <Windows.h>

#include <string>

namespace BO2InjectorHelper
{
    enum class ParseArgumentsStatus
    {
        Success,
        Usage,
        InvalidProcessId
    };

    struct InjectorArguments
    {
        DWORD ProcessId = 0;
        std::wstring DllPath;
    };

    ParseArgumentsStatus ParseInjectorArguments(int argc, wchar_t* argv[], InjectorArguments& arguments);
}
