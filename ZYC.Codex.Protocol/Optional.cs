using System.Text.Json.Serialization;

namespace ZYC.Codex.Protocol;

// ReSharper disable once StructCanBeMadeReadOnly

/// <summary>
///     Distinguishes an omitted property from a specified value, including null.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public struct Optional<T> : IEquatable<Optional<T>>
{
    public Optional(T value)
    {
        StoredValue = value;
        IsSpecified = true;
    }

    private T StoredValue { get; }

    public bool IsSpecified { get; }

    public T Value => IsSpecified
        ? StoredValue
        : throw new InvalidOperationException("The optional value is unspecified.");

    public static Optional<T> Unspecified => default;

    public static implicit operator Optional<T>(T value)
    {
        return new Optional<T>(value);
    }

    public bool Equals(Optional<T> other)
    {
        return IsSpecified == other.IsSpecified &&
               (!IsSpecified || EqualityComparer<T>.Default.Equals(StoredValue, other.StoredValue));
    }

    public override bool Equals(object? obj)
    {
        return obj is Optional<T> other && Equals(other);
    }

    public override int GetHashCode()
    {
        return !IsSpecified
            ? 0
            : unchecked(((StoredValue is null ? 0 : EqualityComparer<T>.Default.GetHashCode(StoredValue)) * 397) ^ 1);
    }

    public static bool operator ==(Optional<T> left, Optional<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Optional<T> left, Optional<T> right)
    {
        return !left.Equals(right);
    }
}