using System;

namespace BO2.Services
{
    internal sealed class DetectedGameChangedEventArgs : EventArgs
    {
        public DetectedGameChangedEventArgs(DetectedGame? detectedGame)
        {
            DetectedGame = detectedGame;
        }

        public DetectedGame? DetectedGame { get; }
    }
}
