// ============================================================================
// Result.cs - Result Pattern Implementation
// ============================================================================
//
// Purpose: Provides a robust Result pattern for better error handling and
// operation outcomes without relying on exceptions for flow control.
//
// Features:
// - Success/failure state tracking
// - Error message collection
// - Generic value return support
// - Fluent API design
// - Implicit conversions for ease of use
//
// ============================================================================

namespace DV.Admin.UI.Infrastructure.Common;

/// <summary>
/// Represents the result of an operation without a return value
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        IsSuccess = isSuccess;
        Error = error;
        Errors = error != null ? new List<string> { error } : new List<string>();
    }

    protected Result(bool isSuccess, IEnumerable<string> errors)
    {
        IsSuccess = isSuccess;
        Errors = errors.ToList();
        Error = Errors.FirstOrDefault();
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }
    public IReadOnlyList<string> Errors { get; }

    // ========================================================================
    // Factory Methods
    // ========================================================================

    /// <summary>
    /// Creates a successful result
    /// </summary>
    public static Result Success() => new(true, (string?)null);

    /// <summary>
    /// Creates a failed result with an error message
    /// </summary>
    public static Result Failure(string error) => new(false, error);

    /// <summary>
    /// Creates a failed result with multiple error messages
    /// </summary>
    public static Result Failure(IEnumerable<string> errors) => new(false, errors);

    /// <summary>
    /// Creates a successful result with a value
    /// </summary>
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    /// <summary>
    /// Creates a failed result with a value type
    /// </summary>
    public static Result<T> Failure<T>(string error) => Result<T>.Failure(error);

    /// <summary>
    /// Creates a failed result with multiple errors and a value type
    /// </summary>
    public static Result<T> Failure<T>(IEnumerable<string> errors) => Result<T>.Failure(errors);

    // ========================================================================
    // Combination Methods
    // ========================================================================

    /// <summary>
    /// Combines multiple results into a single result
    /// </summary>
    public static Result Combine(params Result[] results)
    {
        var failures = results.Where(r => r.IsFailure).ToList();
        
        if (!failures.Any())
            return Success();

        var allErrors = failures.SelectMany(f => f.Errors).ToList();
        return Failure(allErrors);
    }

    /// <summary>
    /// Combines multiple results into a single result
    /// </summary>
    public static Result Combine(IEnumerable<Result> results) 
        => Combine(results.ToArray());

    // ========================================================================
    // Implicit Conversions
    // ========================================================================

    public static implicit operator Result(string error) => Failure(error);

    // ========================================================================
    // Override Methods
    // ========================================================================

    public override string ToString()
    {
        if (IsSuccess)
            return "Success";

        return $"Failure: {string.Join(", ", Errors)}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Result other)
            return false;

        return IsSuccess == other.IsSuccess && 
               Errors.SequenceEqual(other.Errors);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsSuccess, string.Join(",", Errors));
    }
}

/// <summary>
/// Represents the result of an operation with a return value
/// </summary>
public class Result<T> : Result
{
    private readonly T? _value;

    protected Result(T value, bool isSuccess, string? error) 
        : base(isSuccess, error)
    {
        _value = value;
    }

    protected Result(T value, bool isSuccess, IEnumerable<string> errors) 
        : base(isSuccess, errors)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value if the result is successful
    /// </summary>
    public T Value
    {
        get
        {
            if (IsFailure)
                throw new InvalidOperationException("Cannot access value of a failed result. Check IsSuccess before accessing Value.");
            
            return _value!;
        }
    }

    /// <summary>
    /// Gets the value or returns the default value if failed
    /// </summary>
    public T? ValueOrDefault => IsSuccess ? _value : default;

    // ========================================================================
    // Factory Methods
    // ========================================================================

    /// <summary>
    /// Creates a successful result with a value
    /// </summary>
    public static Result<T> Success(T value) => new(value, true, (string?)null);

    /// <summary>
    /// Creates a failed result
    /// </summary>
    public static new Result<T> Failure(string error) => new(default!, false, error);

    /// <summary>
    /// Creates a failed result with multiple errors
    /// </summary>
    public static new Result<T> Failure(IEnumerable<string> errors) => new(default!, false, errors);

    /// <summary>
    /// Creates a result from a nullable value
    /// </summary>
    public static Result<T> FromValue(T? value, string errorMessage = "Value is null")
    {
        return value is not null ? Success(value) : Failure(errorMessage);
    }

    // ========================================================================
    // Implicit Conversions
    // ========================================================================

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(string error) => Failure(error);

    // ========================================================================
    // Override Methods
    // ========================================================================

    public override string ToString()
    {
        if (IsSuccess)
            return $"Success: {_value}";

        return $"Failure: {string.Join(", ", Errors)}";
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Result<T> other)
            return false;

        return IsSuccess == other.IsSuccess && 
               EqualityComparer<T>.Default.Equals(_value, other._value) &&
               Errors.SequenceEqual(other.Errors);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsSuccess, _value, string.Join(",", Errors));
    }
}

/// <summary>
/// Result extensions for async operations
/// </summary>
public static class ResultAsyncExtensions
{
    /// <summary>
    /// Converts a Task of Result to allow async chaining
    /// </summary>
    public static async Task<Result<TOut>> BindAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask, 
        Func<TIn, Task<Result<TOut>>> func)
    {
        var result = await resultTask;
        
        if (result.IsFailure)
            return Result<TOut>.Failure(result.Errors);
        
        return await func(result.Value);
    }

    /// <summary>
    /// Maps a successful result to a new value asynchronously
    /// </summary>
    public static async Task<Result<TOut>> MapAsync<TIn, TOut>(
        this Task<Result<TIn>> resultTask,
        Func<TIn, Task<TOut>> func)
    {
        var result = await resultTask;
        
        if (result.IsFailure)
            return Result<TOut>.Failure(result.Errors);
        
        var value = await func(result.Value);
        return Result<TOut>.Success(value);
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    public static async Task<Result<T>> TapAsync<T>(
        this Task<Result<T>> resultTask,
        Func<T, Task> action)
    {
        var result = await resultTask;
        
        if (result.IsSuccess)
            await action(result.Value);
        
        return result;
    }
}
