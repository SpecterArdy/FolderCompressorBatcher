using System.CommandLine;
using System.CommandLine.Parsing;
using Folder_Compressor_Batcher.Exceptions;
using Folder_Compressor_Batcher.Models;
using Folder_Compressor_Batcher.Services;
using Folder_Compressor_Batcher.Extensions;
using System.CommandLine.Invocation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;

namespace Folder_Compressor_Batcher;

/// <summary>
/// Logger class for the compression application.
/// This non-static class is used for DI-based logging.
/// </summary>
public class CompressionAppLogger
{
    /// <summary>
    /// Empty constructor for DI
    /// </summary>
    public CompressionAppLogger()
    {
    }
}

/// <summary>
/// Main program for the folder compression batcher.
/// </summary>
public static class Program
{
    /// <summary>
    /// Command line options record for parameter bundling.
    /// </summary>
    private record CommandOptions(
        DirectoryInfo Source,
        DirectoryInfo? Output,
        FileInfo? SevenZip,
        int CompressionLevel,
        int? ThreadCount,
        bool Delete,
        bool NoDelete,
        FileInfo? LogFile,
        bool Verbose);
    
    // Default values
    private const string DefaultSevenZipPath = @"C:\Program Files\7-Zip-Zstandard\7z.exe";
    private const int DefaultCompressionLevel = 11;
    private const bool DefaultDeleteAfterCompression = true;
    
    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>Exit code: 0 for success, non-zero for failure.</returns>
    public static async Task<int> Main(string[] args)
    {
        // Configure console
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        // If no arguments provided, run in interactive mode
        if (args.Length == 0)
        {
            return await RunInteractiveMode();
        }
        
        // Create the root command
        var rootCommand = new RootCommand("Folder Compressor Batcher: Compresses folders using 7-Zip with ZSTD compression");
        
        // Source directory option
        var sourceOption = new Option<DirectoryInfo>(
            aliases: new[] { "--source", "-s" },
            description: "Source directory containing folders to compress")
        {
            IsRequired = true
        };
        
        sourceOption.AddValidator(result =>
        {
            var dirInfo = result.GetValueForOption(sourceOption);
            if (dirInfo is null || !dirInfo.Exists)
            {
                result.ErrorMessage = $"Source directory does not exist: {dirInfo?.FullName ?? "null"}";
            }
        });
        rootCommand.AddOption(sourceOption);
        
        // Output directory option
        var outputOption = new Option<DirectoryInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "Output directory for compressed archives",
            getDefaultValue: () => null);
        rootCommand.AddOption(outputOption);
        
        // 7-Zip path option
        var sevenZipOption = new Option<FileInfo?>(
            aliases: new[] { "--7zip", "-z" },
            description: $"Path to the 7-Zip executable (with ZSTD support)",
            getDefaultValue: () => new FileInfo(DefaultSevenZipPath));
        rootCommand.AddOption(sevenZipOption);
        
        // Compression level option
        var compressionLevelOption = new Option<int>(
            aliases: new[] { "--level", "-l" },
            description: "ZSTD compression level (1-22)",
            getDefaultValue: () => DefaultCompressionLevel);
        compressionLevelOption.AddValidator(result =>
        {
            var level = result.GetValueForOption(compressionLevelOption);
            if (level is < 1 or > 22)
            {
                result.ErrorMessage = $"Compression level must be between 1 and 22, got: {level}";
            }
        });
        rootCommand.AddOption(compressionLevelOption);
        
        // Thread count option
        var threadCountOption = new Option<int?>(
            aliases: new[] { "--threads", "-t" },
            description: "Number of threads to use for compression",
            getDefaultValue: () => null);
        threadCountOption.AddValidator(result =>
        {
            var threadCount = result.GetValueForOption(threadCountOption);
            if (threadCount is <= 0)
            {
                result.ErrorMessage = $"Thread count must be positive, got: {threadCount}";
            }
        });
        rootCommand.AddOption(threadCountOption);
        
        // Delete option
        var deleteOption = new Option<bool>(
            aliases: new[] { "--delete", "-d" },
            description: "Delete source folders after successful compression (default: true)",
            getDefaultValue: () => DefaultDeleteAfterCompression);
        rootCommand.AddOption(deleteOption);
        
        // No-delete option
        var noDeleteOption = new Option<bool>(
            aliases: new[] { "--no-delete", "-n" },
            description: "Keep source folders after compression (overrides --delete)",
            getDefaultValue: () => false);
        rootCommand.AddOption(noDeleteOption);
        
        // Log file option
        var logFileOption = new Option<FileInfo?>(
            aliases: new[] { "--log", "-g" },
            description: "Path to the log file",
            getDefaultValue: () => null);
        rootCommand.AddOption(logFileOption);
        
        // Verbose option
        var verboseOption = new Option<bool>(
            aliases: new[] { "--verbose", "-v" },
            description: "Enable verbose logging",
            getDefaultValue: () => false);
        rootCommand.AddOption(verboseOption);
        
        // Register the command handler
        rootCommand.SetHandler(async (InvocationContext context) =>
        {
            var source = context.ParseResult.GetValueForOption(sourceOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption);
            var sevenZip = context.ParseResult.GetValueForOption(sevenZipOption);
            var level = context.ParseResult.GetValueForOption(compressionLevelOption);
            var threads = context.ParseResult.GetValueForOption(threadCountOption);
            var delete = context.ParseResult.GetValueForOption(deleteOption);
            var noDelete = context.ParseResult.GetValueForOption(noDeleteOption);
            var log = context.ParseResult.GetValueForOption(logFileOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);

            var options = new CommandOptions(source, output, sevenZip, level, threads, 
                delete, noDelete, log, verbose);
            int exitCode = 0;
            
            // Setup cancellation token
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // Prevent the process from terminating immediately
                cts.Cancel();
                Console.WriteLine("Cancellation requested. Stopping gracefully...");
            };
            
            // Setup services
            var services = ConfigureServices(
                sourcePath: options.Source.FullName,
                outputPath: options.Output?.FullName ?? Path.Combine(options.Source.FullName, "Output"),
                sevenZipPath: options.SevenZip?.FullName ?? DefaultSevenZipPath,
                compressionLevel: options.CompressionLevel,
                threadCount: options.ThreadCount,
                delete: options.NoDelete ? false : options.Delete, // no-delete overrides delete
                logFilePath: options.LogFile?.FullName ?? Path.Combine(options.Source.FullName, $"compression_{DateTime.Now:yyyyMMdd_HHmmss}.log"),
                verbose: options.Verbose);
            
            try
            {
                await using var serviceProvider = services.BuildServiceProvider();
                
                // Get the logger for the program
                var logger = serviceProvider.GetRequiredService<ILogger<CompressionAppLogger>>();
                logger.LogInformation("Starting compression batch process");
                
                // Log configuration
                logger.LogInformation("Configuration: Source={Source}, Output={Output}, 7-Zip={SevenZip}, Level={Level}, Threads={Threads}, Delete={Delete}",
                    options.Source.FullName,
                    options.Output?.FullName ?? Path.Combine(options.Source.FullName, "Output"),
                    options.SevenZip?.FullName ?? DefaultSevenZipPath,
                    options.CompressionLevel,
                    options.ThreadCount ?? Math.Max(1, Environment.ProcessorCount - 1),
                    options.NoDelete ? false : options.Delete);
                
                // Run the compression process
                var result = await RunCompressionAsync(serviceProvider, options.Source.FullName, cts.Token);
                exitCode = result ? 0 : 1;
            }
            catch (OperationCanceledException)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Operation was cancelled by the user.");
                Console.ResetColor();
                exitCode = 130; // Standard exit code for SIGINT
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Unhandled exception: {ex.Message}");
                Console.ResetColor();
                exitCode = 1;
            }
            
            Environment.ExitCode = exitCode;
        });
        
        // Update the program description
        rootCommand.Description = "Folder Compressor Batcher: Compresses folders using 7-Zip with ZSTD compression.\n" + 
                                  "Specify a source directory with --source to process folders.";
        
        // Parse and execute
        return await rootCommand.InvokeAsync(args);
    }
    
    /// <summary>
    /// Configures the dependency injection container.
    /// </summary>
    /// <param name="sourcePath">Source directory path.</param>
    /// <param name="outputPath">Output directory path.</param>
    /// <param name="sevenZipPath">Path to 7-Zip executable.</param>
    /// <param name="compressionLevel">ZSTD compression level.</param>
    /// <param name="threadCount">Thread count for compression.</param>
    /// <param name="delete">Whether to delete source folders.</param>
    /// <param name="logFilePath">Path to the log file.</param>
    /// <param name="verbose">Whether to enable verbose logging.</param>
    /// <returns>ServiceCollection.</returns>
    private static ServiceCollection ConfigureServices(
        string sourcePath,
        string outputPath,
        string sevenZipPath,
        int compressionLevel,
        int? threadCount,
        bool delete,
        string logFilePath,
        bool verbose)
    {
        var services = new ServiceCollection();
        
        // Get unique log file path
        var uniqueLogPath = Path.Combine(
            Path.GetDirectoryName(logFilePath)!,
            $"{Path.GetFileNameWithoutExtension(logFilePath)}_{Guid.NewGuid():N}{Path.GetExtension(logFilePath)}");
        
        // Configure logging
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = false;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                options.UseUtcTimestamp = false;
                options.ColorBehavior = LoggerColorBehavior.Enabled;
            });
            
            builder.AddFile(uniqueLogPath);
            
            builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        });
        
        // Register configuration
        // We already created a unique log file path above, now just set it in the configuration
        var uniqueLogFilePath = uniqueLogPath;
        
        var config = new CompressionConfig
        {
            SevenZipPath = sevenZipPath,
            OutputDirectory = outputPath,
            CompressionLevel = compressionLevel,
            ThreadCount = threadCount ?? Math.Max(1, Environment.ProcessorCount - 1),
            DeleteAfterCompression = delete,
            LogFilePath = uniqueLogFilePath
        };
        
        services.AddSingleton(config);
        
        // Register compression service
        services.AddSingleton<CompressionService>();
        
        return services;
    }
    
/// <summary>
/// Runs the compression process.
/// </summary>
/// <param name="serviceProvider">Service provider.</param>
/// <param name="sourcePath">Source directory path.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>True if successful, false otherwise.</returns>
/// <summary>
/// Validates the relationship between source and output directories to prevent recursive or unsafe operations.
/// </summary>
/// <param name="sourcePath">Source directory path.</param>
/// <param name="outputPath">Output directory path.</param>
/// <param name="errorMessage">Output error message if validation fails.</param>
/// <returns>True if the relationship is valid, false otherwise.</returns>
private static bool ValidateDirectoryRelationship(string sourcePath, string outputPath, out string? errorMessage)
{
    errorMessage = null;
    var outputDirInfo = new DirectoryInfo(outputPath);
    var sourceDirInfo = new DirectoryInfo(sourcePath);

    var sourcePathNorm = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar);
    var outputPathNorm = Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar);

    // Remove trailing directory separator for accurate path comparison
    if (sourcePathNorm.EndsWith(Path.DirectorySeparatorChar))
        sourcePathNorm = sourcePathNorm.TrimEnd(Path.DirectorySeparatorChar);
    if (outputPathNorm.EndsWith(Path.DirectorySeparatorChar))
        outputPathNorm = outputPathNorm.TrimEnd(Path.DirectorySeparatorChar);

    // Check if directories are the same
    if (string.Equals(sourcePathNorm, outputPathNorm, StringComparison.OrdinalIgnoreCase))
    {
        errorMessage = "Output directory cannot be the same as source directory";
        return false;
    }

    // Check if output is inside source
    if (outputPathNorm.StartsWith(sourcePathNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        errorMessage = "Output directory cannot be inside source directory\nThis could lead to recursive compression attempts.";
        return false;
    }

    // Check if source is inside output
    if (sourcePathNorm.StartsWith(outputPathNorm + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        errorMessage = "Source directory cannot be inside output directory\nThis could lead to data loss.";
        return false;
    }

    return true;
}

/// <summary>
/// Runs the compression process.
/// </summary>
/// <param name="serviceProvider">Service provider.</param>
/// <param name="sourcePath">Source directory path.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>True if successful, false otherwise.</returns>
private static async Task<bool> RunCompressionAsync(
    ServiceProvider serviceProvider,
    string sourcePath,
    CancellationToken cancellationToken)
{
    var compressionService = serviceProvider.GetRequiredService<CompressionService>();
    var logger = serviceProvider.GetRequiredService<ILogger<CompressionAppLogger>>();
    var config = serviceProvider.GetRequiredService<CompressionConfig>();

    // Display configuration details
    Console.WriteLine($"Source directory: {sourcePath}");
    Console.WriteLine($"Output directory: {config.OutputDirectory}");
    Console.WriteLine($"Deletion enabled: {config.DeleteAfterCompression}");
    Console.WriteLine();

    // Directory checks
    if (!Directory.Exists(sourcePath))
    {
        logger.LogError("Source directory does not exist: {Path}", sourcePath);
        Console.WriteLine($"Error: Source directory does not exist: {sourcePath}");
        return false;
    }

    // Directory safety checks
    if (sourcePath.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase) ||
        sourcePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
    {
        logger.LogError("Cannot process system directories for safety reasons");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error: Cannot process system directories for safety reasons");
        Console.ResetColor();
        return false;
    }

    var sourceDir = new DirectoryInfo(sourcePath);
    if (sourceDir.Parent == null || 
        sourceDir.FullName.Split(Path.DirectorySeparatorChar).Length < 3)
    {
        logger.LogError("Cannot process root or shallow directories for safety reasons");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Error: Cannot process root or shallow directories for safety reasons");
        Console.WriteLine("Please specify a directory that is at least two levels deep from the root");
        Console.ResetColor();
        return false;
    }

    // Check for common development paths that should be protected
    string[] protectedPaths = {
        "Program Files",
        "Program Files (x86)",
        "Users\\All Users",
        "Windows",
        "ProgramData",
        "AppData"
    };

    foreach (var protectedPath in protectedPaths)
    {
        if (sourcePath.Contains(protectedPath, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Source directory contains a protected path: {ProtectedPath}", protectedPath);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARNING: Source directory contains a potentially sensitive path: {protectedPath}");
            Console.WriteLine("Are you sure you want to continue? (y/N)");
            Console.ResetColor();
            
            var key = Console.ReadKey(intercept: false);
            Console.WriteLine();
            
            if (char.ToLower(key.KeyChar) != 'y')
            {
                logger.LogInformation("Operation cancelled due to protected path");
                Console.WriteLine("Operation cancelled. No folders were processed.");
                return true;
            }
            
            break;
        }
    }

    // Validate directory relationship
    if (!ValidateDirectoryRelationship(sourcePath, config.OutputDirectory, out var dirErrorMessage))
    {
        logger.LogError(dirErrorMessage);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {dirErrorMessage}");
        Console.ResetColor();
        return false;
    }

    // Get all directories in the source folder
    var directories = sourceDir
        .GetDirectories()
        .Where(d => d.Name != "Output") // Skip the output directory
        .ToList();
    
    if (directories.Count == 0)
    {
        logger.LogWarning("No folders found to compress in: {SourcePath}", sourcePath);
        Console.WriteLine("No folders found to compress.");
        return true;
    }
    
    logger.LogInformation("Found {Count} folders to compress", directories.Count);
    Console.WriteLine($"Found {directories.Count} folders to compress in: {sourcePath}");
    Console.WriteLine();
    
    // Display folders to be processed
    Console.WriteLine("The following folders will be processed:");
    foreach (var dir in directories)
    {
        Console.WriteLine($"  - {dir.Name}");
    }
    Console.WriteLine();
    
    // Ask for confirmation
    if (!ConfirmOperation(directories.Count, config))
    {
        logger.LogInformation("Operation cancelled by user");
        Console.WriteLine("Operation cancelled. No folders were processed.");
        return true;
    }
    
    // Track statistics
    var stats = CompressionStats.Create(directories.Count);
        
    // Process each directory
    foreach (var directory in directories)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            
            Console.WriteLine();
            Console.WriteLine($"Processing {stats.ProcessedCount + 1}/{stats.TotalFolders}: {directory.Name}");
            
            try
            {
                // Setup progress reporting
                var progress = new Progress<CompressionResult>(result =>
                {
                    UpdateProgress(result);
                });
                
                // Compress the folder
                var result = await compressionService.CompressFolderAsync(
                    directory.FullName,
                    progress,
                    cancellationToken);
                
                // Update statistics
                stats = stats.WithResult(result);
                
                // Print the final result
                PrintFolderResult(result);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing folder {FolderName}", directory.Name);
                
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                
                stats = stats.WithFailure();
            }
        }
        
        // Print summary
        PrintSummary(stats);
        
        // Return success if all folders were processed successfully
        return stats.FailedCount == 0;
    }
    
    /// <summary>
    /// Updates the progress display.
    /// </summary>
    /// <param name="result">Compression result.</param>
    private static void UpdateProgress(CompressionResult result)
    {
        // Only show intermediate progress for pending operations
        if (result.Status != CompressionStatus.Pending)
        {
            return;
        }
        
        // For pending operations with source size, show compression progress
        if (result.SourceSize > 0)
        {
            Console.Write($"\rCalculated source size: {FormatSize(result.SourceSize)}");
        }
    }
    
    /// <summary>
    /// Prints the result of a folder compression.
    /// </summary>
    /// <param name="result">Compression result.</param>
    private static void PrintFolderResult(CompressionResult result)
    {
        Console.WriteLine();
        
        if (result.Status == CompressionStatus.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✓ Success: {result.FolderName}");
            Console.ResetColor();
            
            Console.WriteLine($"  Source: {FormatSize(result.SourceSize)}");
            Console.WriteLine($"  Archive: {FormatSize(result.ArchiveSize)}");
            Console.WriteLine($"  Ratio: {result.CompressionRatio:F2}x ({result.CompressionPercentage:F1}% reduction)");
            Console.WriteLine($"  Duration: {FormatDuration(result.Duration)}");
            
            if (result.SourceDeleted)
            {
                Console.WriteLine("  Source folder deleted");
            }
        }
        else if (result.Status == CompressionStatus.Failed)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"× Failed: {result.FolderName}");
            Console.WriteLine($"  Error: {result.ErrorMessage}");
            Console.ResetColor();
        }
        else if (result.Status == CompressionStatus.Skipped)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠ Skipped: {result.FolderName}");
            Console.ResetColor();
        }
        else if (result.Status == CompressionStatus.Cancelled)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"• Cancelled: {result.FolderName}");
            Console.ResetColor();
        }
    }
    
    /// <summary>
    /// Prints a summary of the compression operations.
    /// </summary>
    /// <param name="stats">Statistics about the operations.</param>
    private static void PrintSummary(CompressionStats stats)
    {
        
        Console.WriteLine("=== Compression Summary ===");
        Console.WriteLine($"Total folders: {stats.TotalFolders}");
        Console.WriteLine($"Processed: {stats.ProcessedCount}");
        Console.WriteLine($"Successful: {stats.SuccessCount}");
        
        if (stats.FailedCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed: {stats.FailedCount}");
            Console.ResetColor();
        }
        
        if (stats.SkippedCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Skipped: {stats.SkippedCount}");
            Console.ResetColor();
        }
        
        // Only show size statistics if we have successful compressions
        if (stats.SuccessCount > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Total source size: {FormatSize(stats.TotalSourceSize)}");
            Console.WriteLine($"Total archive size: {FormatSize(stats.TotalArchiveSize)}");
            
            if (stats.TotalSourceSize > 0 && stats.TotalArchiveSize > 0)
            {
                double ratio = (double)stats.TotalSourceSize / stats.TotalArchiveSize;
                double percentage = (1 - (double)stats.TotalArchiveSize / stats.TotalSourceSize) * 100;
                
                Console.WriteLine($"Overall compression ratio: {ratio:F2}x ({percentage:F1}% reduction)");
            }
        }
    }
    
    /// <summary>
    /// Formats a file size in human-readable format.
    /// </summary>
    /// <param name="bytes">Size in bytes.</param>
    /// <returns>Formatted size string.</returns>
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        
        return $"{len:0.##} {sizes[order]}";
    }
    
    /// <summary>
    /// Formats a time span in human-readable format.
    /// </summary>
    /// <param name="timeSpan">Time span to format.</param>
    /// <returns>Formatted duration string.</returns>
    private static string FormatDuration(TimeSpan timeSpan)
    {
        if (timeSpan.TotalSeconds < 1)
        {
            return $"{timeSpan.TotalMilliseconds:0} ms";
        }
        
        if (timeSpan.TotalMinutes < 1)
        {
            return $"{timeSpan.TotalSeconds:0.#} seconds";
        }
        
        if (timeSpan.TotalHours < 1)
        {
            return $"{timeSpan.Minutes} minutes, {timeSpan.Seconds} seconds";
        }
        
        return $"{(int)timeSpan.TotalHours} hours, {timeSpan.Minutes} minutes";
    }
    
    /// <summary>
    /// Asks for user confirmation before proceeding with the operation.
    /// </summary>
    /// <param name="folderCount">Number of folders to be processed.</param>
    /// <param name="config">Compression configuration.</param>
    /// <returns>True if the user confirms, false otherwise.</returns>
    private static bool ConfirmOperation(int folderCount, CompressionConfig config)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("WARNING: This operation will compress folders using 7-Zip with ZSTD compression.");
        
        if (config.DeleteAfterCompression)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("DANGER: SOURCE FOLDER DELETION IS ENABLED!");
            Console.WriteLine("        Original folders will be PERMANENTLY DELETED after successful compression.");
            Console.WriteLine("        Use --no-delete or -n flag to disable deletion if this is not intended.");
            Console.ForegroundColor = ConsoleColor.Yellow;
        }
        else
        {
            Console.WriteLine("        Source folders will be preserved after compression.");
        }
        
        Console.WriteLine("        This operation cannot be undone once started.");
        Console.ResetColor();
        
        Console.WriteLine();
        Console.WriteLine($"Do you want to proceed with processing {folderCount} folder(s)? Type 'yes' to confirm:");
        
        var response = Console.ReadLine()?.Trim().ToLower();
        return response == "yes";
    }
    
    /// <summary>
    /// Runs the application in interactive mode, prompting the user for input.
    /// </summary>
    /// <returns>Exit code: 0 for success, non-zero for failure.</returns>
    private static async Task<int> RunInteractiveMode()
    {
        Console.WriteLine("Folder Compressor Batcher: Interactive Mode");
        Console.WriteLine("=========================================");
        Console.WriteLine();
        
        Console.WriteLine("This tool compresses folders using 7-Zip with ZSTD compression.");
        Console.WriteLine("It can optionally delete source folders after successful compression.");
        Console.WriteLine();
        
        // Get source directory
        Console.Write("Enter source directory path (or 'exit' to quit): ");
        var sourcePath = Console.ReadLine()?.Trim();
        
        if (string.IsNullOrEmpty(sourcePath) || sourcePath.ToLower() == "exit")
        {
            return 0;
        }

        // Validate directory exists
        if (!Directory.Exists(sourcePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError: Directory does not exist: {sourcePath}");
            Console.ResetColor();
            return 1;
        }

        // Ask about output directory
        Console.Write("\nEnter output directory (leave blank for default): ");
        var outputPath = Console.ReadLine()?.Trim();
        
        // If no output directory specified, create a default path
        if (string.IsNullOrEmpty(outputPath))
        {
            var sourceDir = new DirectoryInfo(sourcePath);
            if (sourceDir.Parent == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\nError: Cannot determine default output directory for root path");
                Console.WriteLine("Please specify an output directory explicitly");
                Console.ResetColor();
                return 1;
            }
            
            // Create output directory as a sibling to the source directory with timestamp
            outputPath = Path.Combine(
                sourceDir.Parent.FullName,
                $"{sourceDir.Name}_Compressed_{DateTime.Now:yyyyMMddHHmmss}");
            
            Console.WriteLine($"\nUsing default output directory: {outputPath}");
        }
        
        // Validate directory relationships before attempting to create
        if (!ValidateDirectoryRelationship(sourcePath, outputPath, out var dirErrorMessage))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError: {dirErrorMessage}");
            Console.ResetColor();
            return 1;
        }
        
        // Create output directory if it doesn't exist
        if (!Directory.Exists(outputPath))
        {
            try
            {
                Directory.CreateDirectory(outputPath);
                Console.WriteLine($"Created output directory: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nError: Could not create output directory: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }
        
        // Ask about compression level
        Console.Write("\nEnter compression level (1-22, blank for default 11): ");
        var levelInput = Console.ReadLine()?.Trim();
        int? level = null;
        if (!string.IsNullOrEmpty(levelInput) && int.TryParse(levelInput, out var parsedLevel))
        {
            if (parsedLevel >= 1 && parsedLevel <= 22)
            {
                level = parsedLevel;
            }
            else
            {
                Console.WriteLine("Invalid level. Using default (11).");
            }
        }
        
        // Ask about deletion preference
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\nWARNING: Deletion will permanently remove source folders after successful compression.");
        Console.ResetColor();
        Console.WriteLine("Do you want to delete source folders after successful compression? (y/N):");
        var deleteResponse = Console.ReadLine()?.Trim().ToLower() ?? "n";
        var deleteFlag = deleteResponse.StartsWith("y");

        Console.WriteLine(deleteFlag 
            ? "\nDeletion is ENABLED - source folders will be deleted after successful compression." 
            : "\nDeletion is DISABLED - source folders will be preserved.");

        // Generate a unique log file path
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var processId = Environment.ProcessId;
        var logFilePath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!, 
            $"compression_{timestamp}_{processId}.log");
        
        // Build argument list
        var argsList = new List<string> { "--source", sourcePath };
        
        // Always include the output path (it's either user-specified or default)
        argsList.AddRange(new[] { "--output", outputPath });
        
        if (level.HasValue)
        {
            argsList.AddRange(new[] { "--level", level.Value.ToString() });
        }
        
        argsList.Add(deleteFlag ? "--delete" : "--no-delete");
        
        // Add log file path
        argsList.AddRange(new[] { "--log", logFilePath });
        
        return await Main(argsList.ToArray());
    }
}
