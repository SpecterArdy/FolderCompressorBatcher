using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using Folder_Compressor_Batcher.Exceptions;
using Folder_Compressor_Batcher.Models;
using Microsoft.Extensions.Logging;

namespace Folder_Compressor_Batcher.Services;

/// <summary>
/// Service for compressing folders using 7-Zip with ZSTD compression.
/// </summary>
public sealed class CompressionService : IAsyncDisposable
{
    private readonly CompressionConfig _config = null!;
    private readonly ILogger<CompressionService> _logger = null!;
    private readonly SemaphoreSlim _processLock = new(1, 1);
    private readonly CancellationTokenSource _disposalCts = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionService"/> class.
    /// </summary>
    /// <param name="config">Compression configuration.</param>
    /// <param name="logger">Logger instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when any parameter is null.</exception>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public CompressionService(CompressionConfig config, ILogger<CompressionService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Validate the configuration
        config.Validate();
        
        _logger.LogInformation("Compression service initialized with ZSTD level {Level} using {Threads} threads",
            _config.CompressionLevel, _config.ThreadCount);
        
        // Ensure output directory exists
        if (!Directory.Exists(_config.OutputDirectory))
        {
            _logger.LogInformation("Creating output directory: {OutputDirectory}", _config.OutputDirectory);
            Directory.CreateDirectory(_config.OutputDirectory);
        }
    }

    /// <summary>
    /// Compresses a single folder.
    /// </summary>
    /// <param name="folderPath">Path to the folder to compress.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Compression result.</returns>
    /// <exception cref="ArgumentException">Thrown when folder path is invalid.</exception>
    /// <exception cref="OperationCanceledException">Thrown when operation is cancelled.</exception>
    /// <exception cref="CompressionException">Thrown when compression fails.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    public async Task<CompressionResult> CompressFolderAsync(
        string folderPath,
        IProgress<CompressionResult>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // Link the cancellation tokens
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposalCts.Token);
        
        var linkedToken = linkedCts.Token;
        
        // Validate folder path
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path cannot be empty", nameof(folderPath));
        }
        
        if (!Directory.Exists(folderPath))
        {
            throw new ArgumentException($"Folder does not exist: {folderPath}", nameof(folderPath));
        }
        
        // Get folder information
        var directoryInfo = new DirectoryInfo(folderPath);
        var folderName = directoryInfo.Name;
        
        _logger.LogInformation("Starting compression of folder: {FolderName}", folderName);
        
        // Initialize result
        var result = new CompressionResult(
            FolderName: folderName,
            SourcePath: folderPath,
            ArchivePath: null,
            Status: CompressionStatus.Pending,
            StartTime: DateTimeOffset.Now);
        
        // Report initial progress
        progress?.Report(result);
        
        try
        {
            // Calculate source folder size
            var sourceSize = await CalculateDirectorySizeAsync(folderPath, linkedToken);
            result = result with { SourceSize = sourceSize };
            progress?.Report(result);
            
            _logger.LogInformation("Source folder size: {Size} bytes", sourceSize);
            
            // Create TAR file path
            var tarFilePath = Path.Combine(_config.OutputDirectory, $"{folderName}.tar");
            
            // Create ZSTD archive path
            var zstdFilePath = Path.Combine(_config.OutputDirectory, $"{folderName}.tar.zst");
            
            // Step 1: Create TAR archive
            _logger.LogInformation("Creating TAR archive: {TarPath}", tarFilePath);
            await CreateTarArchiveAsync(folderPath, tarFilePath, linkedToken);
            
            // Step 2: Compress with ZSTD
            _logger.LogInformation("Compressing with ZSTD level {Level}: {ZstdPath}", 
                _config.CompressionLevel, zstdFilePath);
            await CompressWithZstdAsync(tarFilePath, zstdFilePath, linkedToken);
            
            // Step 3: Get archive size
            var archiveInfo = new FileInfo(zstdFilePath);
            var archiveSize = archiveInfo.Length;
            
            _logger.LogInformation("Archive size: {Size} bytes", archiveSize);
            
            // Step 4: Verify archive
            _logger.LogInformation("Verifying archive integrity: {ZstdPath}", zstdFilePath);
            var verificationPassed = await VerifyArchiveAsync(zstdFilePath, linkedToken);
            
            if (!verificationPassed)
            {
                throw new ArchiveVerificationException($"Archive verification failed: {zstdFilePath}");
            }
            
            _logger.LogInformation("Archive verification passed");
            
            // Step 5: Remove TAR file
            if (File.Exists(tarFilePath))
            {
                _logger.LogInformation("Removing temporary TAR file: {TarPath}", tarFilePath);
                File.Delete(tarFilePath);
            }
            
            // Step 6: Update result
            result = result.WithSuccess(zstdFilePath, archiveSize, verificationPassed);
            progress?.Report(result);
            
            // Step 7: Delete source folder if configured
            if (_config.DeleteAfterCompression)
            {
                _logger.LogInformation("Deleting source folder: {FolderPath}", folderPath);
                
                try
                {
                    Directory.Delete(folderPath, recursive: true);
                    result = result.WithSourceDeleted(true);
                    _logger.LogInformation("Source folder deleted successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete source folder: {FolderPath}", folderPath);
                }
                
                progress?.Report(result);
            }
            
            _logger.LogInformation("Compression completed successfully for folder: {FolderName}", folderName);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Compression operation cancelled for folder: {FolderName}", folderName);
            result = result.WithCancelled();
            progress?.Report(result);
            throw;
        }
        catch (Exception ex) when (ex is not CompressionException)
        {
            _logger.LogError(ex, "Compression failed for folder: {FolderName}", folderName);
            result = result.WithError(ex.Message);
            progress?.Report(result);
            throw new CompressionException($"Failed to compress folder: {folderName}", ex);
        }
    }

    /// <summary>
    /// Creates a TAR archive from a folder.
    /// </summary>
    /// <param name="folderPath">Path to the folder.</param>
    /// <param name="tarFilePath">Path to the TAR file to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SevenZipException">Thrown when 7-Zip execution fails.</exception>
    private async Task CreateTarArchiveAsync(
        string folderPath,
        string tarFilePath,
        CancellationToken cancellationToken)
    {
        var arguments = $"a -ttar \"{tarFilePath}\" \"{folderPath}\"";
        
        await ExecuteSevenZipAsync(arguments, "TAR archive creation", cancellationToken);
    }

    /// <summary>
    /// Compresses a TAR file with ZSTD.
    /// </summary>
    /// <param name="tarFilePath">Path to the TAR file.</param>
    /// <param name="zstdFilePath">Path to the ZSTD file to create.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SevenZipException">Thrown when 7-Zip execution fails.</exception>
    private async Task CompressWithZstdAsync(
        string tarFilePath,
        string zstdFilePath,
        CancellationToken cancellationToken)
    {
        var arguments = $"a -m0=zstd -mx={_config.CompressionLevel} -mmt={_config.ThreadCount} \"{zstdFilePath}\" \"{tarFilePath}\"";
        
        await ExecuteSevenZipAsync(arguments, "ZSTD compression", cancellationToken);
    }

    /// <summary>
    /// Verifies an archive's integrity.
    /// </summary>
    /// <param name="archivePath">Path to the archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if archive verification passed, false otherwise.</returns>
    private async Task<bool> VerifyArchiveAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        try
        {
            var arguments = $"t \"{archivePath}\"";
            
            await ExecuteSevenZipAsync(arguments, "Archive verification", cancellationToken);
            
            return true;
        }
        catch (SevenZipException ex)
        {
            _logger.LogError(ex, "Archive verification failed: {ArchivePath}", archivePath);
            return false;
        }
    }

    /// <summary>
    /// Executes 7-Zip with the specified arguments.
    /// </summary>
    /// <param name="arguments">Command line arguments.</param>
    /// <param name="operationName">Name of the operation for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="SevenZipException">Thrown when 7-Zip execution fails.</exception>
    private async Task ExecuteSevenZipAsync(
        string arguments,
        string operationName,
        CancellationToken cancellationToken)
    {
        // Acquire the process lock to ensure only one 7-Zip process runs at a time
        await _processLock.WaitAsync(cancellationToken);
        
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _config.SevenZipPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    outputBuilder.AppendLine(e.Data);
                }
            };
            
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    errorBuilder.AppendLine(e.Data);
                }
            };
            
            _logger.LogDebug("Executing 7-Zip: {Arguments}", arguments);
            
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            // Create a task to wait for the process to exit
            var processTask = process.WaitForExitAsync(cancellationToken);
            
            // Add a timeout
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30), cancellationToken);
            
            // Wait for either the process to exit or the timeout to occur
            var completedTask = await Task.WhenAny(processTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                try
                {
                    // Try to kill the process if it's still running
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to kill 7-Zip process after timeout");
                }
                
                throw new SevenZipException($"7-Zip operation timed out: {operationName}");
            }
            
            // Wait for the process to fully exit to ensure output is captured
            await processTask;
            
            var exitCode = process.ExitCode;
            var standardOutput = outputBuilder.ToString();
            var standardError = errorBuilder.ToString();
            
            if (exitCode != 0)
            {
                throw new SevenZipException(
                    $"7-Zip operation failed: {operationName}. Exit code: {exitCode}",
                    exitCode,
                    standardError);
            }
            
            _logger.LogDebug("7-Zip operation completed successfully: {OperationName}", operationName);
        }
        finally
        {
            _processLock.Release();
        }
    }

    /// <summary>
    /// Calculates the size of a directory.
    /// </summary>
    /// <param name="directoryPath">Path to the directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Size of the directory in bytes.</returns>
    private async Task<long> CalculateDirectorySizeAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var directoryInfo = new DirectoryInfo(directoryPath);
            return CalculateDirectorySizeInternal(directoryInfo, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Calculates the size of a directory recursively.
    /// </summary>
    /// <param name="directoryInfo">Directory information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Size of the directory in bytes.</returns>
    private long CalculateDirectorySizeInternal(
        DirectoryInfo directoryInfo,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        long size = 0;
        
        // Sum up all files in the current directory
        try
        {
            foreach (var fileInfo in directoryInfo.GetFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                size += fileInfo.Length;
            }
            
            // Recursively sum up all subdirectories
            foreach (var subDirInfo in directoryInfo.GetDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                size += CalculateDirectorySizeInternal(subDirInfo, cancellationToken);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unable to access directory or file in {DirectoryPath}", directoryInfo.FullName);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error when calculating directory size for {DirectoryPath}", directoryInfo.FullName);
        }
        
        return size;
    }
    
    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if this instance has been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when this instance has been disposed.</exception>
    [MemberNotNull(nameof(_config), nameof(_logger))]
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(_config), nameof(_logger))]
    private void ThrowIfDisposed([CallerMemberName] string? callerName = null)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                GetType().Name,
                $"Cannot access the {callerName} method because this instance has been disposed.");
        }
        
        // Explicitly check for null to satisfy the compiler
        if (_config is null || _logger is null)
        {
            throw new InvalidOperationException("Service was not properly initialized");
        }
    }
    
    /// <summary>
    /// Asynchronously releases all resources used by this instance.
    /// </summary>
    /// <returns>A task representing the asynchronous dispose operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        
        try
        {
            // Signal all operations to cancel
            _disposalCts.Cancel();
            
            // Wait a short time for operations to complete
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during disposal of CompressionService");
        }
        finally
        {
            // Dispose managed resources
            _disposalCts.Dispose();
            _processLock.Dispose();
            
            _disposed = true;
        }
    }
}
