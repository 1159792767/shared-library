namespace Company.UpdateChecker;

/// <summary>
/// 轻量 SemVer 2.0.0 版本号：主.次.修订[-预发布][+构建元数据]。
/// 构建元数据不参与比较（用于剥离编译产物 InformationalVersion 携带的 commit 哈希）。
/// </summary>
public sealed partial class SemVersion :
    IComparable<SemVersion>, IEquatable<SemVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }
    public bool IsPrerelease => Prerelease is not null;

    private SemVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public static SemVersion Parse(string input)
    {
        if (!TryParse(input, out var version))
        {
            throw new FormatException($"非法的 SemVer 版本字符串：\"{input}\"（期望格式：主.次.修订[-预发布][+构建元数据]）");
        }

        return version!;
    }

    public static bool TryParse(string? input, out SemVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var span = input.AsSpan();

        // 剥离构建元数据（+后缀），不参与比较
        var plusIndex = span.IndexOf('+');
        if (plusIndex >= 0)
        {
            span = span[..plusIndex];
        }

        // 剥离预发布标识（-后缀），只从核心部分中找
        var dashIndex = span.IndexOf('-');
        var prerelease = (string?)null;
        ReadOnlySpan<char> core = span;
        if (dashIndex >= 0)
        {
            prerelease = span[(dashIndex + 1)..].ToString();
            core = span[..dashIndex];

            if (prerelease.Length == 0 || !IsValidPrerelease(prerelease))
            {
                return false;
            }
        }

        // 核心三段：主.次.修订
        var remaining = core;
        var major = ParseSegment(ref remaining);
        var minor = ParseSegment(ref remaining);
        if (remaining.IsEmpty)
        {
            return false;
        }

        var patchSpan = remaining;
        var patch = 0;
        var consumed = 0;
        if (!TryParseDigits(patchSpan, out patch, out consumed) || consumed != patchSpan.Length)
        {
            return false;
        }

        version = new SemVersion(major, minor, patch, prerelease);
        return true;
    }

    private static int ParseSegment(ref ReadOnlySpan<char> span)
    {
        var consumed = 0;
        if (!TryParseDigits(span, out var value, out consumed))
        {
            return -1;
        }

        span = span[consumed..];
        if (span.IsEmpty || span[0] != '.')
        {
            span = ReadOnlySpan<char>.Empty;
        }
        else
        {
            span = span[1..];
        }

        return value;
    }

    private static bool TryParseDigits(ReadOnlySpan<char> span, out int value, out int consumed)
    {
        value = 0;
        consumed = 0;
        while (consumed < span.Length && char.IsAsciiDigit(span[consumed]))
        {
            value = value * 10 + (span[consumed] - '0');
            consumed++;
        }

        return consumed > 0;
    }

    private static bool IsValidPrerelease(string prerelease)
    {
        // 预发布标识以点分段，每段非空且仅允许字母、数字、连字符
        foreach (var segment in prerelease.Split('.'))
        {
            if (segment.Length == 0)
            {
                return false;
            }

            foreach (var c in segment)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    public int CompareTo(SemVersion? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }

        if (other is null)
        {
            return 1; // 非 null 大于 null
        }

        // 核心版本号逐段比较
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // 预发布规则：正式版 > 预发布版
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    private static int ComparePrerelease(string? left, string? right)
    {
        // SemVer 2.0.0 #11：无预发布标识 > 有预发布标识
        if (left is null && right is null) return 0;
        if (left is null) return 1;
        if (right is null) return -1;

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');

        var count = Math.Min(leftParts.Length, rightParts.Length);
        for (var i = 0; i < count; i++)
        {
            var l = leftParts[i];
            var r = rightParts[i];
            var lIsNum = IsNumeric(l);
            var rIsNum = IsNumeric(r);

            // 数字标识符 < 字母数字标识符
            if (lIsNum && rIsNum)
            {
                var result = CompareNumeric(l, r);
                if (result != 0) return result;
            }
            else if (lIsNum != rIsNum)
            {
                return lIsNum ? -1 : 1;
            }
            else
            {
                var result = string.CompareOrdinal(l, r);
                if (result != 0) return result;
            }
        }

        // 前缀段全相等：段数多的大
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static bool IsNumeric(string segment) =>
        segment.Length > 0 && segment.All(char.IsAsciiDigit);

    private static int CompareNumeric(string left, string right) =>
        long.Parse(left).CompareTo(long.Parse(right));

    public bool Equals(SemVersion? other) =>
        other is not null
        && Major == other.Major
        && Minor == other.Minor
        && Patch == other.Patch
        && string.Equals(Prerelease, other.Prerelease, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as SemVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() =>
        IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    public static bool operator <(SemVersion? left, SemVersion? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator >(SemVersion? left, SemVersion? right) =>
        right is null ? left is not null : right.CompareTo(left) < 0;

    public static bool operator <=(SemVersion? left, SemVersion? right) =>
        left is null ? right is not null : left.CompareTo(right) <= 0;

    public static bool operator >=(SemVersion? left, SemVersion? right) =>
        right is null ? left is not null : right.CompareTo(left) <= 0;

    public static bool operator ==(SemVersion? left, SemVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemVersion? left, SemVersion? right) => !(left == right);
}
