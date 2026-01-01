namespace A3ITranslator.Application.Common;

/// <summary>
/// Simple result pattern for error handling
/// </summary>
public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Value { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public Exception? Exception { get; private set; }

    private Result(bool isSuccess, T? value, string errorMessage, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public static Result<T> Success(T value)
    {
        return new Result<T>(true, value, string.Empty);
    }

    public static Result<T> Failure(string errorMessage)
    {
        return new Result<T>(false, default, errorMessage);
    }

    public static Result<T> Failure(string errorMessage, Exception exception)
    {
        return new Result<T>(false, default, errorMessage, exception);
    }

    public static Result<T> Failure(Exception exception)
    {
        return new Result<T>(false, default, exception.Message, exception);
    }
}

/// <summary>
/// Simple result pattern without value
/// </summary>
public class Result
{
    public bool IsSuccess { get; private set; }
    public string ErrorMessage { get; private set; } = string.Empty;
    public Exception? Exception { get; private set; }

    private Result(bool isSuccess, string errorMessage, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        Exception = exception;
    }

    public static Result Success()
    {
        return new Result(true, string.Empty);
    }

    public static Result Failure(string errorMessage)
    {
        return new Result(false, errorMessage);
    }

    public static Result Failure(string errorMessage, Exception exception)
    {
        return new Result(false, errorMessage, exception);
    }

    public static Result Failure(Exception exception)
    {
        return new Result(false, exception.Message, exception);
    }
}
