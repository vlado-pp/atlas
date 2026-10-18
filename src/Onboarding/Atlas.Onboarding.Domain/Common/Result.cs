namespace Atlas.Onboarding.Domain.Common;

public enum ErrorKind
{
    Validation,
    Conflict,
}

/// <summary>An expected business failure. Exceptions are reserved for bugs and infrastructure failures.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind, string? Field = null)
{
    public static Error Validation(string field, string code, string message) => new(code, message, ErrorKind.Validation, field);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
}

public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(Error error) => new(error);

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Result has no value: {Error!.Message}");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error) => new(default, error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);
}
