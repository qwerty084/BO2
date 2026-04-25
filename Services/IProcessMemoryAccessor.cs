using System;

namespace BO2.Services
{
    internal interface IProcessMemoryAccessor : IDisposable
    {
        void Attach(int processId, string processName);

        int ReadInt32(uint address, string valueName);

        float ReadSingle(uint address, string valueName);

        void Close();
    }
}
