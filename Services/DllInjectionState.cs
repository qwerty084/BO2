namespace BO2.Services
{
    public enum DllInjectionState
    {
        NotAttempted = 0,
        UnsupportedGame,
        MonitorDllMissing,
        WrongProcessArchitecture,
        AlreadyInjected,
        Injected,
        Failed
    }
}
