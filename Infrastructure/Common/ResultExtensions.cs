// ============================================================================
// ResultExtensions.cs - Result Pattern Extension Methods
// ============================================================================
//
// Purpose: Provides extension methods for the Result pattern to enable
// fluent API design and functional programming patterns.
//
// Features:
// - Monadic bind operations
// - Map transformations
// - Conditional operations
// - Error handling utilities
// - Fluent chaining support
//
// ============================================================================

namespace DV.Admin.UI.Infrastructure.Common;

/// <summary>
/// Extension methods for Result pattern
/// </summary>
public static class ResultExtensions
{
    // ========================================================================
    // Bind Operations (Monadic)
    // ========================================================================

    /// <summary>
    /// Binds a function that returns a Result to a Result, enabling chaining
    /// </summary>
    public static Result Bind(this Result result, Func<Result> func)
    {
        return result.IsFailure ? result : func();
    }

    /// <summary>
    /// Binds a function that returns a Result<T> to a Result, enabling chaining
    /// </summary>
    public static Result<T> Bind<T>(this Result result, Func<Result<T>> func)
    {
        return result.IsFailure 
            ? Result<T>.Failure(result.Errors) 
            : func();
    }

    /// <summary>
    /// Binds a function that takes the result value and returns a new Result
    /// </summary>
    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> func)
    {
        return result.IsFailure 
            ? Result<TOut>.Failure(result.Errors) 
            : func(result.Value);
    }

    /// <summary>
    /// Binds a function that takes the result value and returns a Result without value
    /// </summary>
    public static Result Bind<T>(this Result<T> result, Func<T, Result> func)
    {
        return result.IsFailure 
            ? Result.Failure(result.Errors) 
            : func(result.Value);
    }

    // ========================================================================
    // Map Operations (Functor)
    // ========================================================================

    /// <summary>
    /// Maps a successful result to a new value
    /// </summary>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> func)
    {
        return result.IsFailure 
            ? Result<TOut>.Failure(result.Errors) 
            : Result<TOut>.Success(func(result.Value));
    }

    /// <summary>
    /// Maps a successful result to a new value with error handling
    /// </summary>
    public static Result<TOut> MapTry<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> func, string? errorMessage = null)
    {
        if (result.IsFailure)
            return Result<TOut>.Failure(result.Errors);

        try
        {
            var value = func(result.Value);
            return Result<TOut>.Success(value);
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? $"Error during mapping: {ex.Message}";
            return Result<TOut>.Failure(message);
        }
    }

    // ========================================================================
    // Conditional Operations
    // ========================================================================

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        if (result.IsSuccess)
            action(result.Value);
        
        return result;
    }

    /// <summary>
    /// Executes an action if the result is successful
    /// </summary>
    public static Result Tap(this Result result, Action action)
    {
        if (result.IsSuccess)
            action();
        
        return result;
    }

    /// <summary>
    /// Executes an action if the result failed
    /// </summary>
    public static Result<T> TapError<T>(this Result<T> result, Action<IEnumerable<string>> action)
    {
        if (result.IsFailure)
            action(result.Errors);
        
        return result;
    }

    /// <summary>
    /// Executes an action if the result failed
    /// </summary>
    public static Result TapError(this Result result, Action<IEnumerable<string>> action)
    {
        if (result.IsFailure)
            action(result.Errors);
        
        return result;
    }

    // ========================================================================
    // Error Handling
    // ========================================================================

    /// <summary>
    /// Provides a fallback value if the result failed
    /// </summary>
    public static T OnFailure<T>(this Result<T> result, T fallbackValue)
    {
        return result.IsSuccess ? result.Value : fallbackValue;
    }

    /// <summary>
    /// Provides a fallback value using a function if the result failed
    /// </summary>
    public static T OnFailure<T>(this Result<T> result, Func<T> fallbackFunc)
    {
        return result.IsSuccess ? result.Value : fallbackFunc();
    }

    /// <summary>
    /// Provides a fallback result if the current result failed
    /// </summary>
    public static Result<T> OnFailure<T>(this Result<T> result, Func<IEnumerable<string>, Result<T>> fallbackFunc)
    {
        return result.IsSuccess ? result : fallbackFunc(result.Errors);
    }

    /// <summary>
    /// Adds additional context to error messages
    /// </summary>
    public static Result<T> WithContext<T>(this Result<T> result, string context)
    {
        if (result.IsSuccess)
            return result;

        var contextualErrors = result.Errors.Select(error => $"{context}: {error}").ToList();
        return Result<T>.Failure(contextualErrors);
    }

    /// <summary>
    /// Adds additional context to error messages
    /// </summary>
    public static Result WithContext(this Result result, string context)
    {
        if (result.IsSuccess)
            return result;

        var contextualErrors = result.Errors.Select(error => $"{context}: {error}").ToList();
        return Result.Failure(contextualErrors);
    }

    // ========================================================================
    // Validation Extensions
    // ========================================================================

    /// <summary>
    /// Ensures the result value meets a condition
    /// </summary>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, string errorMessage)
    {
        if (result.IsFailure)
            return result;

        return predicate(result.Value) 
            ? result 
            : Result<T>.Failure(errorMessage);
    }

    /// <summary>
    /// Ensures the result value is not null
    /// </summary>
    public static Result<T> EnsureNotNull<T>(this Result<T?> result, string errorMessage = "Value cannot be null")
        where T : class
    {
        return result.Bind(value => 
            value != null 
                ? Result<T>.Success(value) 
                : Result<T>.Failure(errorMessage));
    }

    // ========================================================================
    // Collection Operations
    // ========================================================================

    /// <summary>
    /// Combines multiple results into a single result containing all values
    /// </summary>
    public static Result<IEnumerable<T>> Combine<T>(this IEnumerable<Result<T>> results)
    {
        var resultList = results.ToList();
        var failures = resultList.Where(r => r.IsFailure).ToList();

        if (failures.Any())
        {
            var allErrors = failures.SelectMany(f => f.Errors).ToList();
            return Result<IEnumerable<T>>.Failure(allErrors);
        }

        var values = resultList.Select(r => r.Value).ToList();
        return Result<IEnumerable<T>>.Success(values);
    }

    /// <summary>
    /// Filters successful results and returns their values
    /// </summary>
    public static IEnumerable<T> SuccessfulValues<T>(this IEnumerable<Result<T>> results)
    {
        return results.Where(r => r.IsSuccess).Select(r => r.Value);
    }

    /// <summary>
    /// Gets all error messages from failed results
    /// </summary>
    public static IEnumerable<string> AllErrors<T>(this IEnumerable<Result<T>> results)
    {
        return results.Where(r => r.IsFailure).SelectMany(r => r.Errors);
    }

    // ========================================================================
    // Pattern Matching
    // ========================================================================

    /// <summary>
    /// Pattern matching for Result with return value
    /// </summary>
    public static TOut Match<TIn, TOut>(this Result<TIn> result, 
        Func<TIn, TOut> onSuccess, 
        Func<IEnumerable<string>, TOut> onFailure)
    {
        return result.IsSuccess 
            ? onSuccess(result.Value) 
            : onFailure(result.Errors);
    }

    /// <summary>
    /// Pattern matching for Result with side effects
    /// </summary>
    public static void Match<T>(this Result<T> result, 
        Action<T> onSuccess, 
        Action<IEnumerable<string>> onFailure)
    {
        if (result.IsSuccess)
            onSuccess(result.Value);
        else
            onFailure(result.Errors);
    }

    /// <summary>
    /// Pattern matching for Result without value
    /// </summary>
    public static TOut Match<TOut>(this Result result, 
        Func<TOut> onSuccess, 
        Func<IEnumerable<string>, TOut> onFailure)
    {
        return result.IsSuccess 
            ? onSuccess() 
            : onFailure(result.Errors);
    }

    // ========================================================================
    // Try Operations
    // ========================================================================

    /// <summary>
    /// Wraps an operation in a try-catch and returns a Result
    /// </summary>
    public static Result<T> Try<T>(Func<T> operation, string? errorMessage = null)
    {
        try
        {
            var value = operation();
            return Result<T>.Success(value);
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? ex.Message;
            return Result<T>.Failure(message);
        }
    }

    /// <summary>
    /// Wraps an async operation in a try-catch and returns a Result
    /// </summary>
    public static async Task<Result<T>> TryAsync<T>(Func<Task<T>> operation, string? errorMessage = null)
    {
        try
        {
            var value = await operation();
            return Result<T>.Success(value);
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? ex.Message;
            return Result<T>.Failure(message);
        }
    }

    /// <summary>
    /// Wraps an operation that doesn't return a value
    /// </summary>
    public static Result Try(Action operation, string? errorMessage = null)
    {
        try
        {
            operation();
            return Result.Success();
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? ex.Message;
            return Result.Failure(message);
        }
    }

    /// <summary>
    /// Wraps an async operation that doesn't return a value
    /// </summary>
    public static async Task<Result> TryAsync(Func<Task> operation, string? errorMessage = null)
    {
        try
        {
            await operation();
            return Result.Success();
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? ex.Message;
            return Result.Failure(message);
        }
    }
}
