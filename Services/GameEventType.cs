namespace BO2.Services
{
    public enum GameEventType
    {
        Unknown = 0,
        StartOfRound,
        EndOfRound,
        PowerUpGrabbed,
        DogRoundStarting,
        PowerOn,
        EndGame,
        PerkBought,
        RoundChanged,
        PointsChanged,
        KillsChanged,
        DownsChanged,
        NotifyCandidateRejected,
        NotifyEntryCandidate,
        StringResolverCandidate,
        NotifyObserved,
        BoxEvent
    }
}
