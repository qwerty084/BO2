using System;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BO2.Services
{
    public static class AppStrings
    {
        private static readonly ResourceLoader ResourceLoader = new();

        public static string Get(string resourceId)
        {
            string value = ResourceLoader.GetString(resourceId);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Missing string resource '{resourceId}'.");
            }

            return value;
        }

        public static string Format(string resourceId, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, Get(resourceId), arguments);
        }
    }
}
