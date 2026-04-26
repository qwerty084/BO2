namespace BO2.Services
{
    public sealed record DllInjectionResult(
        DllInjectionState State,
        string Message,
        string? DllPath = null)
    {
        public static DllInjectionResult NotAttempted { get; } = new(
            DllInjectionState.NotAttempted,
            AppStrings.Get("DllInjectionNotAttempted"));
    }
}
