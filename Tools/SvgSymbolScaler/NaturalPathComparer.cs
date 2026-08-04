using System.Globalization;

namespace SvgSymbolScaler;

public sealed class NaturalPathComparer : IComparer<string>
{
    public static NaturalPathComparer OrdinalIgnoreCase { get; } = new(StringComparer.OrdinalIgnoreCase);

    private readonly StringComparer _textComparer;

    private NaturalPathComparer(StringComparer textComparer) => _textComparer = textComparer;

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var left = Path.GetFileName(x);
        var right = Path.GetFileName(y);
        var i = 0;
        var j = 0;

        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                var leftStart = i;
                var rightStart = j;
                while (i < left.Length && char.IsDigit(left[i])) i++;
                while (j < right.Length && char.IsDigit(right[j])) j++;

                var leftDigits = left.AsSpan(leftStart, i - leftStart);
                var rightDigits = right.AsSpan(rightStart, j - rightStart);
                var leftTrimmed = TrimLeadingZeros(leftDigits);
                var rightTrimmed = TrimLeadingZeros(rightDigits);

                var lengthComparison = leftTrimmed.Length.CompareTo(rightTrimmed.Length);
                if (lengthComparison != 0) return lengthComparison;

                var numberComparison = leftTrimmed.SequenceCompareTo(rightTrimmed);
                if (numberComparison != 0) return numberComparison;

                // Equal numeric value: prefer fewer leading zeroes, e.g. page2 before page002.
                var rawLengthComparison = leftDigits.Length.CompareTo(rightDigits.Length);
                if (rawLengthComparison != 0) return rawLengthComparison;
                continue;
            }

            var leftStartText = i;
            var rightStartText = j;
            while (i < left.Length && !char.IsDigit(left[i])) i++;
            while (j < right.Length && !char.IsDigit(right[j])) j++;

            var textComparison = _textComparer.Compare(
                left[leftStartText..i],
                right[rightStartText..j]);
            if (textComparison != 0) return textComparison;
        }

        var remainderComparison = (left.Length - i).CompareTo(right.Length - j);
        if (remainderComparison != 0) return remainderComparison;

        // Stable deterministic tie-breaker for identical file names in different subfolders.
        return _textComparer.Compare(x, y);
    }

    private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> value)
    {
        var index = 0;
        while (index < value.Length - 1 && value[index] == '0') index++;
        return value[index..];
    }
}
