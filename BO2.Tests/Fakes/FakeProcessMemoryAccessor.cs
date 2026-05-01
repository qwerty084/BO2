using System;
using System.Collections.Generic;
using BO2.Services;

namespace BO2.Tests.Fakes
{
    internal sealed class FakeProcessMemoryAccessor : IProcessMemoryAccessor
    {
        private readonly Dictionary<uint, int> _int32Values = [];
        private readonly Dictionary<uint, float> _singleValues = [];
        private readonly Dictionary<uint, Exception> _int32Exceptions = [];
        private readonly Dictionary<uint, Exception> _singleExceptions = [];

        public int DefaultInt32Value { get; set; }

        public float DefaultSingleValue { get; set; }

        public Exception? AttachException { get; set; }

        public Action<int, string>? AttachCallback { get; set; }

        public int AttachCallCount { get; private set; }

        public int CloseCallCount { get; private set; }

        public void SetInt32(uint address, int value)
        {
            _int32Values[address] = value;
        }

        public void SetSingle(uint address, float value)
        {
            _singleValues[address] = value;
        }

        public void SetInt32Exception(uint address, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _int32Exceptions[address] = exception;
        }

        public void SetSingleException(uint address, Exception exception)
        {
            ArgumentNullException.ThrowIfNull(exception);
            _singleExceptions[address] = exception;
        }

        public void Attach(int processId, string processName)
        {
            AttachCallCount++;
            AttachCallback?.Invoke(processId, processName);

            if (AttachException is not null)
            {
                throw AttachException;
            }
        }

        public int ReadInt32(uint address, string valueName)
        {
            if (_int32Exceptions.TryGetValue(address, out Exception? exception))
            {
                throw exception;
            }

            return _int32Values.TryGetValue(address, out int value) ? value : DefaultInt32Value;
        }

        public float ReadSingle(uint address, string valueName)
        {
            if (_singleExceptions.TryGetValue(address, out Exception? exception))
            {
                throw exception;
            }

            return _singleValues.TryGetValue(address, out float value) ? value : DefaultSingleValue;
        }

        public void Close()
        {
            CloseCallCount++;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
