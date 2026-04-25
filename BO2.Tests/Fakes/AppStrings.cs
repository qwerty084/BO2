using System.Globalization;

// Stub that satisfies AppStrings call-sites in linked source files without pulling in
// the WinAppSDK ResourceLoader. Returns the resource key as-is for Get(), and applies
// the key as a plain format string for Format() so structured assertions still work.
namespace BO2.Services
{
    internal static class AppStrings
    {
        public static string Get(string resourceId)
        {
            return resourceId;
        }

        public static string Format(string resourceId, params object[] arguments)
        {
            if (arguments.Length == 0)
            {
                return resourceId;
            }

            // Use the resourceId itself as the template so callers get a predictable string.
            // Tests that care about exact status text should assert on ConnectionState instead.
            return $"{resourceId}({string.Join(", ", arguments)})";
        }
    }
}
