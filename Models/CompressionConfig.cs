using System.Text.Json.Serialization;

namespace Folder_Compressor_Batcher.Models;

/// <summary>
/// Represents the immutable configuration for the compression operation.
/// </summary>
public sealed record CompressionConfig
{
    /// <summary>
    /// Path to the 7-Zip executable.
    /// </summary>
    public required string SevenZipPath { get; init; }

    /// <summary>
    /// Output directory where compressed archives will be stored.
    /// </summary>
    public required string OutputDirectory { get; init; }

    /// <summary>
    /// ZSTD compression level (1-22).
    /// </summary>
    public int CompressionLevel { get; init; } = 11;

    /// <summary>
    /// Number of threads to use for compression.
    /// </summary>
    public int ThreadCount { get; init; } = Math.Max(1, Environment.ProcessorCount - 1);

    // Log file path removed

    /// <summary>
    /// Whether to delete source folders after successful compression.
    /// </summary>
    public bool DeleteAfterCompression { get; init; } = true;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SevenZipPath))
            throw new ArgumentException("7-Zip path cannot be empty", nameof(SevenZipPath));

        if (!File.Exists(SevenZipPath))
            throw new ArgumentException($"7-Zip executable not found at: {SevenZipPath}", nameof(SevenZipPath));

        if (string.IsNullOrWhiteSpace(OutputDirectory))
            throw new ArgumentException("Output directory cannot be empty", nameof(OutputDirectory));

        if (CompressionLevel is < 1 or > 22)
            throw new ArgumentException($"Compression level must be between 1 and 22, got: {CompressionLevel}", nameof(CompressionLevel));

        if (ThreadCount < 1)
            throw new ArgumentException($"Thread count must be at least 1, got: {ThreadCount}", nameof(ThreadCount));
    }
}

