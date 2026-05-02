using System;
using System.Collections.Generic;

namespace BO2.Services
{
    internal abstract record DisplayText
    {
        public static DisplayText Plain(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            return new PlainText(text);
        }

        public static DisplayText Resource(string resourceId)
        {
            ArgumentNullException.ThrowIfNull(resourceId);

            return new ResourceText(resourceId);
        }

        public static DisplayText Format(string resourceId, params object[] arguments)
        {
            ArgumentNullException.ThrowIfNull(resourceId);
            ArgumentNullException.ThrowIfNull(arguments);

            return new FormatText(resourceId, Array.AsReadOnly((object[])arguments.Clone()));
        }

        public static DisplayText Lines(params DisplayText[] lines)
        {
            ArgumentNullException.ThrowIfNull(lines);

            return new LinesText(Array.AsReadOnly((DisplayText[])lines.Clone()));
        }

        public static DisplayText Integer(int value)
        {
            return new IntegerText(value);
        }

        public static DisplayText Float2(float value)
        {
            return new Float2Text(value);
        }

        public static DisplayText Address(uint value)
        {
            return new AddressText(value);
        }

        public static DisplayText LocalTime(DateTimeOffset value)
        {
            return new LocalTimeText(value);
        }

        public sealed record PlainText(string Text) : DisplayText;

        public sealed record ResourceText(string ResourceId) : DisplayText;

        public sealed record FormatText(string ResourceId, IReadOnlyList<object> Arguments) : DisplayText;

        public sealed record LinesText(IReadOnlyList<DisplayText> Items) : DisplayText;

        public sealed record IntegerText(int Value) : DisplayText;

        public sealed record Float2Text(float Value) : DisplayText;

        public sealed record AddressText(uint Value) : DisplayText;

        public sealed record LocalTimeText(DateTimeOffset Value) : DisplayText;
    }
}
