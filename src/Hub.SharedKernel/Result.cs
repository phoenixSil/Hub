namespace Hub.SharedKernel;

public class Result
{
    protected Result(bool isSuccess, string? error, IReadOnlyDictionary<string, string>? fieldErrors)
    {
        IsSuccess = isSuccess;
        Error = error;
        FieldErrors = fieldErrors ?? new Dictionary<string, string>();
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public IReadOnlyDictionary<string, string> FieldErrors { get; }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string error) => new(false, error, null);
    public static Result Invalid(IReadOnlyDictionary<string, string> fieldErrors) =>
        new(false, "Certains champs sont invalides.", fieldErrors);
}

public sealed class Result<T> : Result
{
    private Result(T? value, bool isSuccess, string? error, IReadOnlyDictionary<string, string>? fieldErrors)
        : base(isSuccess, error, fieldErrors) => Value = value;

    public T? Value { get; }

    public static Result<T> Success(T value) => new(value, true, null, null);
    public static new Result<T> Failure(string error) => new(default, false, error, null);
    public static new Result<T> Invalid(IReadOnlyDictionary<string, string> fieldErrors) =>
        new(default, false, "Certains champs sont invalides.", fieldErrors);
}
