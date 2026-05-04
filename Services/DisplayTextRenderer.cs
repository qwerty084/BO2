using System;
using System.Globalization;
using System.Linq;

namespace BO2.Services
{
    internal sealed class DisplayTextRenderer
    {
        public string Render(DisplayText text)
        {
            ArgumentNullException.ThrowIfNull(text);

            return text switch
            {
                DisplayText.PlainText plain => plain.Text,
                DisplayText.ResourceText resource => AppStrings.Get(resource.ResourceId),
                DisplayText.FormatText format => AppStrings.Format(
                    format.ResourceId,
                    [.. format.Arguments.Select(RenderArgument)]),
                DisplayText.LinesText lines => string.Join(
                    Environment.NewLine,
                    lines.Items.Select(Render)),
                DisplayText.IntegerText integer => integer.Value.ToString("N0", CultureInfo.CurrentCulture),
                DisplayText.Float2Text float2 => float2.Value.ToString("N2", CultureInfo.CurrentCulture),
                DisplayText.AddressText address => $"0x{address.Value:X8}",
                DisplayText.LocalTimeText localTime => localTime.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                _ => throw new InvalidOperationException($"Unsupported display text type '{text.GetType().Name}'.")
            };
        }

        private object RenderArgument(object argument)
        {
            return argument is DisplayText text
                ? Render(text)
                : argument;
        }
    }
}
