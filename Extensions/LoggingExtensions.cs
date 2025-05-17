using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Folder_Compressor_Batcher.Extensions;

/// <summary>
/// Extensions for configuring logging.
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Adds a file logger provider to the logging builder.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="filePath">File path for logging.</param>
    /// <returns>The logging builder with file logging configured.</returns>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string filePath)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        builder.Services.Configure<FileLoggerOptions>(options =>
        {
            options.FilePath = filePath;
            options.FileSizeLimitBytes = 10 * 1024 * 1024; // 10 MB
            options.MaxRollingFiles = 3;
        });
        
        builder.AddProvider(new FileLoggerProvider(
            builder.Services.BuildServiceProvider().GetRequiredService<IOptions<FileLoggerOptions>>()));
        
        return builder;
    }
    
    /// <summary>
    /// Adds a file logger provider with custom options.
    /// </summary>
    /// <param name="builder">The logging builder.</param>
    /// <param name="configure">Action to configure options.</param>
    /// <returns>The logging builder with file logging configured.</returns>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, Action<FileLoggerOptions> configure)
    {
        builder.Services.Configure(configure);
        builder.AddProvider(new FileLoggerProvider(
            builder.Services.BuildServiceProvider().GetRequiredService<IOptions<FileLoggerOptions>>()));
        
        return builder;
    }
}

/// <summary>
/// Options for the file logger.
/// </summary>
public class FileLoggerOptions
{
    /// <summary>
    /// Gets or sets the file path.
    /// </summary>
    public string FilePath { get; set; } = "app.log";
    
    /// <summary>
    /// Gets or sets the file size limit in bytes.
    /// </summary>
    public long FileSizeLimitBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    
    /// <summary>
    /// Gets or sets the maximum number of rolling files.
    /// </summary>
    public int MaxRollingFiles { get; set; } = 3;
    
    /// <summary>
    /// Gets or sets the format of the log entries.
    /// </summary>
    public string LogFormat { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level}] {Category}: {Message}{NewLine}{Exception}";
}

/// <summary>
/// File logger provider.
/// </summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLoggerOptions _options;
    private readonly Dictionary<string, FileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="FileLoggerProvider"/> class.
    /// </summary>
    /// <param name="options">Logger options.</param>
    public FileLoggerProvider(IOptions<FileLoggerOptions> options)
    {
        _options = options.Value;
    }
    
    /// <summary>
    /// Creates a new logger for the category.
    /// </summary>
    /// <param name="categoryName">Category name.</param>
    /// <returns>A logger instance.</returns>
    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileLoggerProvider));
        }
        
        lock (_lock)
        {
            if (!_loggers.TryGetValue(categoryName, out var logger))
            {
                logger = new FileLogger(categoryName, _options);
                _loggers[categoryName] = logger;
            }
            
            return logger;
        }
    }
    
    /// <summary>
    /// Disposes this instance and any resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            
            foreach (var logger in _loggers.Values)
            {
                logger.Dispose();
            }
            
            _loggers.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// File logger implementation.
/// </summary>
public sealed class FileLogger : ILogger, IDisposable
{
    private readonly string _categoryName;
    private readonly FileLoggerOptions _options;
    private readonly object _lock = new();
    private FileStream? _fileStream;
    private StreamWriter? _streamWriter;
    private bool _disposed;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="FileLogger"/> class.
    /// </summary>
    /// <param name="categoryName">Category name.</param>
    /// <param name="options">Logger options.</param>
    public FileLogger(string categoryName, FileLoggerOptions options)
    {
        _categoryName = categoryName;
        _options = options;
    }
    
    /// <summary>
    /// Begins a new logging scope.
    /// </summary>
    /// <typeparam name="TState">State type.</typeparam>
    /// <param name="state">State.</param>
    /// <returns>Disposable scope.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }
    
    /// <summary>
    /// Checks if the logger is enabled for the specified level.
    /// </summary>
    /// <param name="logLevel">Log level.</param>
    /// <returns>True if enabled, false otherwise.</returns>
    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }
    
    /// <summary>
    /// Logs a message.
    /// </summary>
    /// <typeparam name="TState">State type.</typeparam>
    /// <param name="logLevel">Log level.</param>
    /// <param name="eventId">Event ID.</param>
    /// <param name="state">State.</param>
    /// <param name="exception">Exception.</param>
    /// <param name="formatter">Message formatter.</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }
        
        if (formatter == null)
        {
            throw new ArgumentNullException(nameof(formatter));
        }
        
        var message = formatter(state, exception);
        
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }
        
        var formattedMessage = FormatMessage(logLevel, message, exception);
        WriteMessage(formattedMessage);
    }
    
    /// <summary>
    /// Formats a log message.
    /// </summary>
    private string FormatMessage(LogLevel logLevel, string message, Exception? exception)
    {
        var logFormat = _options.LogFormat;
        
        return logFormat
            .Replace("{Timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
            .Replace("{Level}", GetLogLevelString(logLevel))
            .Replace("{Category}", _categoryName)
            .Replace("{Message}", message)
            .Replace("{NewLine}", Environment.NewLine)
            .Replace("{Exception}", exception == null ? string.Empty : $"{Environment.NewLine}{exception}");
    }
    
    /// <summary>
    /// Gets the string representation of a log level.
    /// </summary>
    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT ",
            _ => "NONE "
        };
    }
    
    /// <summary>
    /// Writes a message to the log file.
    /// </summary>
    private void WriteMessage(string message)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            
            for (int retries = 0; retries < 3; retries++)
            {
                try
                {
                    // Ensure the writer is created
                    EnsureWriterCreated();
                    
                    // Check file size and roll if needed
                    CheckForRollFile();
                    
                    // Write the message
                    _streamWriter?.WriteLine(message);
                    _streamWriter?.Flush();
                    return;
                }
                catch (IOException ex)
                {
                    if (retries == 2)
                    {
                        Console.Error.WriteLine($"Error writing to log file after retries: {ex.Message}");
                        return;
                    }
                    Thread.Sleep(100); // Wait before retry
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error writing to log file: {ex.Message}");
                    return;
                }
            }
        }
    }
    
    /// <summary>
    /// Ensures the writer is created.
    /// </summary>
    private void EnsureWriterCreated()
    {
        if (_streamWriter != null)
        {
            return;
        }
        
        try
        {
            var directory = Path.GetDirectoryName(_options.FilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            _fileStream = new FileStream(
                _options.FilePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read);
                
            _streamWriter = new StreamWriter(_fileStream, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error creating log file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Checks if the file should be rolled and rolls it if needed.
    /// </summary>
    private void CheckForRollFile()
    {
        if (_fileStream == null || _streamWriter == null)
        {
            return;
        }
        
        if (_fileStream.Length < _options.FileSizeLimitBytes)
        {
            return;
        }
        
        try
        {
            // Close current file
            _streamWriter.Dispose();
            _fileStream.Dispose();
            _streamWriter = null;
            _fileStream = null;
            
            // Roll files
            RollFiles();
            
            // Create new file
            EnsureWriterCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error rolling log file: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Rolls the log files.
    /// </summary>
    private void RollFiles()
    {
        // Delete the oldest file if it exists
        var oldestFile = GetRolledFileName(_options.MaxRollingFiles);
        if (File.Exists(oldestFile))
        {
            File.Delete(oldestFile);
        }
        
        // Roll all other files
        for (var i = _options.MaxRollingFiles - 1; i >= 1; i--)
        {
            var sourceFile = GetRolledFileName(i);
            var destFile = GetRolledFileName(i + 1);
            
            if (File.Exists(sourceFile))
            {
                File.Move(sourceFile, destFile);
            }
        }
        
        // Roll the current file
        if (File.Exists(_options.FilePath))
        {
            File.Move(_options.FilePath, GetRolledFileName(1));
        }
    }
    
    /// <summary>
    /// Gets the file name for a rolled log file.
    /// </summary>
    private string GetRolledFileName(int index)
    {
        var directory = Path.GetDirectoryName(_options.FilePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(_options.FilePath);
        var extension = Path.GetExtension(_options.FilePath);
        
        return Path.Combine(directory, $"{fileName}.{index}{extension}");
    }
    
    /// <summary>
    /// Disposes this instance and any resources.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            
            _streamWriter?.Dispose();
            _fileStream?.Dispose();
            
            _streamWriter = null;
            _fileStream = null;
            _disposed = true;
        }
    }
    
    /// <summary>
    /// Null scope instance.
    /// </summary>
    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        
        private NullScope() { }
        
        public void Dispose() { }
    }
}

