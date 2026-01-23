// ============================================================================
// ApplicationHealthCheck.cs - Comprehensive Application Health Monitor
// ============================================================================
//
// Purpose: Provides overall application health monitoring including
// service availability, configuration validation, and system resources.
//
// Features:
// - Service dependency validation
// - Configuration health checking
// - System resource monitoring
// - Application-specific health metrics
//
// ============================================================================

using Microsoft.Extensions.Diagnostics.HealthChecks;
using DV.Admin.UI.Services;
using System.Diagnostics;

namespace DV.Admin.UI.Infrastructure.HealthChecks;

/// <summary>
/// Comprehensive application health check
/// </summary>
public class ApplicationHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ApplicationHealthCheck> _logger;

    public ApplicationHealthCheck(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ApplicationHealthCheck> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = new Dictionary<string, object>();
            var healthyComponents = new List<string>();
            var issues = new List<string>();

            // Check service dependencies
            var serviceResult = CheckServiceDependencies();
            data.Add("Services", serviceResult);
            
            if (serviceResult.IsHealthy)
                healthyComponents.Add("Services");
            else
                issues.Add($"Services: {serviceResult.ErrorMessage}");

            // Check configuration
            var configResult = CheckConfiguration();
            data.Add("Configuration", configResult);
            
            if (configResult.IsHealthy)
                healthyComponents.Add("Configuration");
            else
                issues.Add($"Configuration: {configResult.ErrorMessage}");

            // Check system resources
            var resourceResult = await CheckSystemResourcesAsync(cancellationToken);
            data.Add("SystemResources", resourceResult);
            
            if (resourceResult.IsHealthy)
                healthyComponents.Add("SystemResources");
            else
                issues.Add($"SystemResources: {resourceResult.ErrorMessage}");

            // Get application metrics
            var metricsResult = GetApplicationMetrics();
            data.Add("ApplicationMetrics", metricsResult);

            // Overall health determination
            var isHealthy = serviceResult.IsHealthy && configResult.IsHealthy && resourceResult.IsHealthy;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;

            var description = isHealthy 
                ? $"Application healthy: {string.Join(", ", healthyComponents)}"
                : $"Application issues: {string.Join("; ", issues)}";

            _logger.LogInformation("Application health check completed: {Status} - {Description}", 
                status, description);

            return new HealthCheckResult(status, description, null, data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application health check failed with exception");
            return new HealthCheckResult(
                HealthStatus.Unhealthy, 
                "Application health check failed", 
                ex,
                new Dictionary<string, object> { ["Error"] = ex.Message });
        }
    }

    private ServiceHealthResult CheckServiceDependencies()
    {
        try
        {
            var requiredServices = new Dictionary<string, Type>
            {
                { "UserService", typeof(UserService) },
                { "RoleService", typeof(RoleService) },
                { "DocumentUploadService", typeof(DocumentUploadService) },
                { "DocumentUploadResultService", typeof(DocumentUploadResultService) },
                { "AuditService", typeof(AuditService) }
            };

            var availableServices = new List<string>();
            var missingServices = new List<string>();

            foreach (var service in requiredServices)
            {
                try
                {
                    var serviceInstance = _serviceProvider.GetService(service.Value);
                    if (serviceInstance != null)
                        availableServices.Add(service.Key);
                    else
                        missingServices.Add(service.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to resolve service {ServiceName}", service.Key);
                    missingServices.Add($"{service.Key} (Error: {ex.Message})");
                }
            }

            return new ServiceHealthResult
            {
                IsHealthy = missingServices.Count == 0,
                AvailableServices = availableServices,
                MissingServices = missingServices,
                TotalServicesChecked = requiredServices.Count,
                ErrorMessage = missingServices.Count > 0 ? $"Missing services: {string.Join(", ", missingServices)}" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Service dependency check failed");
            return new ServiceHealthResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private ConfigurationHealthResult CheckConfiguration()
    {
        try
        {
            var requiredConfigs = new[]
            {
                "ConnectionStrings:DefaultConnection",
                "ConnectionStrings:SecurityConnection",
                "Logging:LogLevel:Default"
            };

            var availableConfigs = new List<string>();
            var missingConfigs = new List<string>();

            foreach (var config in requiredConfigs)
            {
                var value = _configuration[config];
                if (!string.IsNullOrWhiteSpace(value))
                    availableConfigs.Add(config);
                else
                    missingConfigs.Add(config);
            }

            // Check optional configurations
            var optionalConfigs = new Dictionary<string, string?>();
            optionalConfigs["DocumentUpload:MaxFileSizeBytes"] = _configuration["DocumentUpload:MaxFileSizeBytes"];
            optionalConfigs["Cache:DefaultExpirationMinutes"] = _configuration["Cache:DefaultExpirationMinutes"];

            return new ConfigurationHealthResult
            {
                IsHealthy = missingConfigs.Count == 0,
                AvailableConfigurations = availableConfigs,
                MissingConfigurations = missingConfigs,
                OptionalConfigurations = optionalConfigs,
                ErrorMessage = missingConfigs.Count > 0 ? $"Missing configurations: {string.Join(", ", missingConfigs)}" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Configuration check failed");
            return new ConfigurationHealthResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private async Task<SystemResourceResult> CheckSystemResourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var process = Process.GetCurrentProcess();
            
            // Get memory usage
            var workingSet = process.WorkingSet64;
            var privateMemory = process.PrivateMemorySize64;
            
            // Get CPU usage (approximate)
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;
            
            await Task.Delay(100, cancellationToken); // Brief delay for CPU calculation
            
            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;
            
            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsagePercent = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;

            // Check thresholds
            var memoryThresholdMB = 1024; // 1GB threshold
            var cpuThreshold = 80; // 80% CPU threshold

            var isHealthy = (workingSet / 1024 / 1024) < memoryThresholdMB && cpuUsagePercent < cpuThreshold;

            return new SystemResourceResult
            {
                IsHealthy = isHealthy,
                WorkingSetBytes = workingSet,
                PrivateMemoryBytes = privateMemory,
                CpuUsagePercent = Math.Max(0, cpuUsagePercent), // Ensure non-negative
                ProcessorCount = Environment.ProcessorCount,
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                UptimeSeconds = (DateTime.UtcNow - process.StartTime).TotalSeconds,
                ErrorMessage = !isHealthy ? $"Resource usage high - Memory: {workingSet / 1024 / 1024}MB, CPU: {cpuUsagePercent:F1}%" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System resource check failed");
            return new SystemResourceResult 
            { 
                IsHealthy = false, 
                ErrorMessage = ex.Message 
            };
        }
    }

    private ApplicationMetrics GetApplicationMetrics()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "Unknown";
            
            return new ApplicationMetrics
            {
                ApplicationVersion = version,
                StartupTime = DateTime.UtcNow, // This would normally be tracked from application start
                Environment = _configuration["ASPNETCORE_ENVIRONMENT"] ?? "Unknown",
                MachineName = Environment.MachineName,
                RuntimeVersion = Environment.Version.ToString(),
                WorkingDirectory = Environment.CurrentDirectory
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get application metrics");
            return new ApplicationMetrics 
            { 
                ErrorMessage = ex.Message 
            };
        }
    }
}

#region Result Classes

public class ServiceHealthResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> AvailableServices { get; set; } = new();
    public List<string> MissingServices { get; set; } = new();
    public int TotalServicesChecked { get; set; }
}

public class ConfigurationHealthResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> AvailableConfigurations { get; set; } = new();
    public List<string> MissingConfigurations { get; set; } = new();
    public Dictionary<string, string?> OptionalConfigurations { get; set; } = new();
}

public class SystemResourceResult
{
    public bool IsHealthy { get; set; }
    public string? ErrorMessage { get; set; }
    public long WorkingSetBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public double CpuUsagePercent { get; set; }
    public int ProcessorCount { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public double UptimeSeconds { get; set; }
    
    public string WorkingSetFormatted => FormatBytes(WorkingSetBytes);
    public string PrivateMemoryFormatted => FormatBytes(PrivateMemoryBytes);
    public string UptimeFormatted => TimeSpan.FromSeconds(UptimeSeconds).ToString(@"dd\.hh\:mm\:ss");

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB" };
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}

public class ApplicationMetrics
{
    public string ApplicationVersion { get; set; } = string.Empty;
    public DateTime StartupTime { get; set; }
    public string Environment { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

#endregion
