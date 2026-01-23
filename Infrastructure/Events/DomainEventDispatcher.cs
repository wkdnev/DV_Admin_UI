// ============================================================================
// DomainEventDispatcher.cs - Domain Event Publishing and Handling Infrastructure
// ============================================================================
//
// Purpose: Provides centralized domain event dispatching capabilities,
// enabling decoupled communication between domain components through
// an event-driven architecture pattern.
//
// Features:
// - Asynchronous event publishing and handling
// - Multiple handler support per event type
// - Exception handling and logging
// - Performance monitoring and metrics
// - Thread-safe concurrent operations
//
// Usage:
// - Inject IDomainEventDispatcher into services
// - Publish events after domain operations
// - Register event handlers in DI container
// - Monitor event processing performance
//
// ============================================================================

using DV.Shared.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DV.Admin.UI.Infrastructure.Events;

/// <summary>
/// Interface for domain event dispatching
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Publish a domain event to all registered handlers
    /// </summary>
    /// <typeparam name="TEvent">Type of domain event</typeparam>
    /// <param name="domainEvent">The event to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the publishing operation</returns>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent;

    /// <summary>
    /// Publish multiple domain events in sequence
    /// </summary>
    /// <param name="domainEvents">Collection of events to publish</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the publishing operation</returns>
    Task PublishManyAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get statistics about event processing
    /// </summary>
    /// <returns>Event processing statistics</returns>
    DomainEventStatistics GetStatistics();
}

/// <summary>
/// Implementation of domain event dispatcher using dependency injection
/// </summary>
public class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DomainEventDispatcher> _logger;
    private readonly DomainEventStatistics _statistics;

    public DomainEventDispatcher(
        IServiceProvider serviceProvider,
        ILogger<DomainEventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _statistics = new DomainEventStatistics();
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IDomainEvent
    {
        if (domainEvent == null)
        {
            _logger.LogWarning("Attempted to publish null domain event");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var eventType = typeof(TEvent).Name;

        try
        {
            _logger.LogDebug("Publishing domain event {EventType} with ID {EventId}",
                eventType, domainEvent.EventId);

            // Get all handlers for this event type
            using var scope = _serviceProvider.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IDomainEventHandler<TEvent>>();

            var handlerTasks = new List<Task>();

            foreach (var handler in handlers)
            {
                var handlerType = handler.GetType().Name;
                _logger.LogDebug("Dispatching {EventType} to handler {HandlerType}",
                    eventType, handlerType);

                var handlerTask = HandleEventSafelyAsync(handler, domainEvent, handlerType, cancellationToken);
                handlerTasks.Add(handlerTask);
            }

            if (handlerTasks.Any())
            {
                await Task.WhenAll(handlerTasks);
                _logger.LogDebug("Successfully dispatched {EventType} to {HandlerCount} handlers",
                    eventType, handlerTasks.Count);
            }
            else
            {
                _logger.LogInformation("No handlers registered for domain event {EventType}", eventType);
            }

            _statistics.RecordSuccess(eventType, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish domain event {EventType} with ID {EventId}",
                eventType, domainEvent.EventId);
            _statistics.RecordFailure(eventType, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    public async Task PublishManyAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        if (domainEvents == null)
        {
            _logger.LogWarning("Attempted to publish null domain events collection");
            return;
        }

        var eventsList = domainEvents.ToList();
        if (!eventsList.Any())
        {
            _logger.LogDebug("No domain events to publish");
            return;
        }

        _logger.LogDebug("Publishing {EventCount} domain events", eventsList.Count);

        var publishTasks = eventsList.Select(async domainEvent =>
        {
            // Use reflection to call the generic PublishAsync method
            var eventType = domainEvent.GetType();
            var method = GetType().GetMethod(nameof(PublishAsync))!.MakeGenericMethod(eventType);
            var task = (Task)method.Invoke(this, new object[] { domainEvent, cancellationToken })!;
            await task;
        });

        await Task.WhenAll(publishTasks);
        _logger.LogDebug("Successfully published {EventCount} domain events", eventsList.Count);
    }

    public DomainEventStatistics GetStatistics()
    {
        return _statistics.Clone();
    }

    private async Task HandleEventSafelyAsync<TEvent>(
        IDomainEventHandler<TEvent> handler,
        TEvent domainEvent,
        string handlerType,
        CancellationToken cancellationToken) where TEvent : IDomainEvent
    {
        try
        {
            await handler.HandleAsync(domainEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler {HandlerType} failed to process event {EventType} with ID {EventId}",
                handlerType, typeof(TEvent).Name, domainEvent.EventId);

            // Continue processing other handlers even if one fails
            // In production, you might want to implement retry logic or dead letter queues
        }
    }
}

/// <summary>
/// Statistics about domain event processing
/// </summary>
public class DomainEventStatistics
{
    private readonly object _lock = new();
    private readonly Dictionary<string, EventTypeStatistics> _eventStats = new();

    public void RecordSuccess(string eventType, long durationMs)
    {
        lock (_lock)
        {
            if (!_eventStats.ContainsKey(eventType))
            {
                _eventStats[eventType] = new EventTypeStatistics();
            }

            _eventStats[eventType].RecordSuccess(durationMs);
        }
    }

    public void RecordFailure(string eventType, long durationMs)
    {
        lock (_lock)
        {
            if (!_eventStats.ContainsKey(eventType))
            {
                _eventStats[eventType] = new EventTypeStatistics();
            }

            _eventStats[eventType].RecordFailure(durationMs);
        }
    }

    public DomainEventStatistics Clone()
    {
        lock (_lock)
        {
            var clone = new DomainEventStatistics();
            foreach (var kvp in _eventStats)
            {
                clone._eventStats[kvp.Key] = kvp.Value.Clone();
            }
            return clone;
        }
    }

    public IReadOnlyDictionary<string, EventTypeStatistics> EventTypeStatistics
    {
        get
        {
            lock (_lock)
            {
                return _eventStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone());
            }
        }
    }

    public int TotalEventsProcessed
    {
        get
        {
            lock (_lock)
            {
                return _eventStats.Values.Sum(s => s.SuccessCount + s.FailureCount);
            }
        }
    }

    public int TotalSuccessCount
    {
        get
        {
            lock (_lock)
            {
                return _eventStats.Values.Sum(s => s.SuccessCount);
            }
        }
    }

    public int TotalFailureCount
    {
        get
        {
            lock (_lock)
            {
                return _eventStats.Values.Sum(s => s.FailureCount);
            }
        }
    }

    public double OverallSuccessRate
    {
        get
        {
            var total = TotalEventsProcessed;
            return total == 0 ? 0.0 : (double)TotalSuccessCount / total * 100.0;
        }
    }
}

/// <summary>
/// Statistics for a specific event type
/// </summary>
public class EventTypeStatistics
{
    public int SuccessCount { get; private set; }
    public int FailureCount { get; private set; }
    public long TotalDurationMs { get; private set; }
    public long MinDurationMs { get; private set; } = long.MaxValue;
    public long MaxDurationMs { get; private set; }

    public void RecordSuccess(long durationMs)
    {
        SuccessCount++;
        UpdateDuration(durationMs);
    }

    public void RecordFailure(long durationMs)
    {
        FailureCount++;
        UpdateDuration(durationMs);
    }

    private void UpdateDuration(long durationMs)
    {
        TotalDurationMs += durationMs;
        MinDurationMs = Math.Min(MinDurationMs, durationMs);
        MaxDurationMs = Math.Max(MaxDurationMs, durationMs);
    }

    public double SuccessRate
    {
        get
        {
            var total = SuccessCount + FailureCount;
            return total == 0 ? 0.0 : (double)SuccessCount / total * 100.0;
        }
    }

    public double AverageDurationMs
    {
        get
        {
            var total = SuccessCount + FailureCount;
            return total == 0 ? 0.0 : (double)TotalDurationMs / total;
        }
    }

    public EventTypeStatistics Clone()
    {
        return new EventTypeStatistics
        {
            SuccessCount = SuccessCount,
            FailureCount = FailureCount,
            TotalDurationMs = TotalDurationMs,
            MinDurationMs = MinDurationMs == long.MaxValue ? 0 : MinDurationMs,
            MaxDurationMs = MaxDurationMs
        };
    }
}
