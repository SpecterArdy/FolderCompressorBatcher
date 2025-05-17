using System.Diagnostics.CodeAnalysis;

namespace Folder_Compressor_Batcher.Models;

/// <summary>
/// Represents statistics for compression operations.
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class CompressionStats
{
    /// <summary>
    /// Gets or sets the total number of folders.
    /// </summary>
    public int TotalFolders { get; set; }

    /// <summary>
    /// Gets or sets the number of processed folders.
    /// </summary>
    public int ProcessedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of successfully compressed folders.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Gets or sets the number of failed folders.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Gets or sets the number of skipped folders.
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// Gets or sets the total size of source folders in bytes.
    /// </summary>
    public long TotalSourceSize { get; set; }

    /// <summary>
    /// Gets or sets the total size of archives in bytes.
    /// </summary>
    public long TotalArchiveSize { get; set; }

    /// <summary>
    /// Creates a new instance with the specified values.
    /// </summary>
    /// <param name="totalFolders">Total number of folders.</param>
    /// <returns>A new instance with the specified values.</returns>
    public static CompressionStats Create(int totalFolders)
    {
        return new CompressionStats
        {
            TotalFolders = totalFolders,
            ProcessedCount = 0,
            SuccessCount = 0,
            FailedCount = 0,
            SkippedCount = 0,
            TotalSourceSize = 0,
            TotalArchiveSize = 0
        };
    }

    /// <summary>
    /// Creates a new instance with updated values.
    /// </summary>
    /// <param name="result">Compression result to use for updating.</param>
    /// <returns>A new instance with updated values.</returns>
    public CompressionStats WithResult(CompressionResult result)
    {
        return new CompressionStats
        {
            TotalFolders = this.TotalFolders,
            ProcessedCount = this.ProcessedCount + 1,
            SuccessCount = this.SuccessCount + (result.Status == CompressionStatus.Success ? 1 : 0),
            FailedCount = this.FailedCount + (result.Status == CompressionStatus.Failed ? 1 : 0),
            SkippedCount = this.SkippedCount + (result.Status == CompressionStatus.Skipped ? 1 : 0),
            TotalSourceSize = this.TotalSourceSize + result.SourceSize,
            TotalArchiveSize = this.TotalArchiveSize + result.ArchiveSize
        };
    }

    /// <summary>
    /// Creates a new instance with failure count incremented.
    /// </summary>
    /// <returns>A new instance with failure count incremented.</returns>
    public CompressionStats WithFailure()
    {
        return new CompressionStats
        {
            TotalFolders = this.TotalFolders,
            ProcessedCount = this.ProcessedCount + 1,
            SuccessCount = this.SuccessCount,
            FailedCount = this.FailedCount + 1,
            SkippedCount = this.SkippedCount,
            TotalSourceSize = this.TotalSourceSize,
            TotalArchiveSize = this.TotalArchiveSize
        };
    }
}

