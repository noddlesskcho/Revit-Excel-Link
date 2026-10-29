#if NETFRAMEWORK
using System.Collections.Generic;

namespace System
{
    /// <summary>.NET Framework stand-in for System.Index (used by the compiler for text[..n] on strings).</summary>
    internal readonly struct Index
    {
        private readonly int _value;

        public Index(int value, bool fromEnd = false) => _value = fromEnd ? ~value : value;

        public static Index Start => new(0);

        public static Index End => new(0, fromEnd: true);

        public bool IsFromEnd => _value < 0;

        public int Value => _value < 0 ? ~_value : _value;

        public int GetOffset(int length) => IsFromEnd ? length - Value : Value;

        public static implicit operator Index(int value) => new(value);
    }

    /// <summary>.NET Framework stand-in for System.Range (used by the compiler for text[a..b] on strings).</summary>
    internal readonly struct Range
    {
        public Range(Index start, Index end)
        {
            Start = start;
            End = end;
        }

        public Index Start { get; }

        public Index End { get; }

        public static Range StartAt(Index start) => new(start, Index.End);

        public static Range EndAt(Index end) => new(Index.Start, end);

        public static Range All => new(Index.Start, Index.End);

        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            var start = Start.GetOffset(length);
            var end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            return (start, end - start);
        }
    }
}

namespace RevitScheduleSync
{
    /// <summary>Instance methods that .NET Framework 4.8 lacks; only compiled for Revit 2024.</summary>
    internal static class NetFrameworkExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
            where TKey : notnull
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }

        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }

        public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

        public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;
    }
}
#endif
