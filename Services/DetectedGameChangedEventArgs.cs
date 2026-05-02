using System;

namespace BO2.Services
{
    internal sealed class DetectedGameChangedEventArgs(DetectedGame? detectedGame) : EventArgs
    {
        public DetectedGame? DetectedGame { get; } = detectedGame;
    }
}
