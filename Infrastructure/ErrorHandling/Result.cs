using System.ComponentModel.DataAnnotations;

namespace DV.Admin.UI.Infrastructure.ErrorHandling;

public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Data { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> ValidationErrors { get; private set; } = new();

    private Result() { }

    public static Result<T> Success(T data)
    {
        return new Result<T>
        {
            IsSuccess = true,
            Data = data
        };
    }

    public static Result<T> Failure(string errorMessage)
    {
        return new Result<T>
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    public static Result<T> ValidationFailure(List<ValidationResult> validationResults)
    {
        return new Result<T>
        {
            IsSuccess = false,
            ErrorMessage = "Validation failed",
            ValidationErrors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown validation error").ToList()
        };
    }
}

public class Result
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public List<string> ValidationErrors { get; private set; } = new();

    private Result() { }

    public static Result Success()
    {
        return new Result { IsSuccess = true };
    }

    public static Result Failure(string errorMessage)
    {
        return new Result
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    public static Result ValidationFailure(List<ValidationResult> validationResults)
    {
        return new Result
        {
            IsSuccess = false,
            ErrorMessage = "Validation failed",
            ValidationErrors = validationResults.Select(vr => vr.ErrorMessage ?? "Unknown validation error").ToList()
        };
    }
}
