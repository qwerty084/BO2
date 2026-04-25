using BO2.Services;

namespace BO2.Tests.Fakes
{
    internal sealed class FakeGameProcessDetector : IGameProcessDetector
    {
        public DetectedGame? Result { get; set; }

        public int DetectCallCount { get; private set; }

        public DetectedGame? Detect()
        {
            DetectCallCount++;
            return Result;
        }
    }
}
