#include "InjectorArguments.h"

#include <cerrno>
#include <cwchar>

namespace BO2InjectorHelper
{
    ParseArgumentsStatus ParseInjectorArguments(int argc, wchar_t* argv[], InjectorArguments& arguments)
    {
        arguments = InjectorArguments{};
        if (argc != 3)
        {
            return ParseArgumentsStatus::Usage;
        }

        if (argv[1] == nullptr || argv[1][0] == L'\0' || argv[1][0] == L'-' || argv[1][0] == L'+')
        {
            return ParseArgumentsStatus::InvalidProcessId;
        }

        wchar_t* end = nullptr;
        errno = 0;
        const unsigned long long processIdValue = std::wcstoull(argv[1], &end, 10);
        if (end == argv[1]
            || *end != L'\0'
            || errno == ERANGE
            || processIdValue == 0
            || processIdValue > MAXDWORD)
        {
            return ParseArgumentsStatus::InvalidProcessId;
        }

        arguments.ProcessId = static_cast<DWORD>(processIdValue);
        arguments.DllPath = argv[2] == nullptr ? std::wstring{} : std::wstring(argv[2]);
        return ParseArgumentsStatus::Success;
    }
}
