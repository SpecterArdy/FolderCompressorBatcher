namespace Folder_Compressor_Batcher.Models;

/// <summary>
/// Represents the status of a compression operation.
/// </summary>
public enum CompressionStatus
{
    /// <summary>
    /// The compression operation is pending or in progress.
    /// </summary>
    Pending,

    /// <summary>
    /// The compression operation completed successfully.
    /// </summary>
    Success,

    /// <summary>
    /// The compression operation failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The compression operation was skipped.
    /// </summary>
    Skipped,

    /// <summary>
    /// The compression operation was cancelled.
    /// </summary>
    Cancelled
}

/// <summary>
/// Represents the result of a compression operation.
/// </summary>
/// <param name="FolderName">Name of the folder that was compressed.</param>
/// <param name="SourcePath">Path to the source folder.</param>
/// <param name="ArchivePath">Path to the created archive.</param>
/// <param name="Status">Status of the compression operation.</param>
/// <param name="StartTime">When the operation started.</param>
/// <param name="EndTime">When the operation completed.</param>
/// <param name="SourceSize">Size of the source folder in bytes.</param>
/// <param name="ArchiveSize">Size of the resulting archive in bytes.</param>
/// <param name="VerificationPassed">Whether archive verification passed.</param>
/// <param name="SourceDeleted">Whether the source folder was deleted.</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
public sealed record CompressionResult(
    string FolderName,
    string SourcePath,
    string? ArchivePath,
    CompressionStatus Status,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime = null,
    long SourceSize = 0,
    long ArchiveSize = 0,
    bool VerificationPassed = false,
    bool SourceDeleted = false,
    string? ErrorMessage = null)
{
    /// <summary>
    /// Gets the duration of the compression operation.
    /// </summary>
    public TimeSpan Duration => EndTime.HasValue
        ? EndTime.Value - StartTime
        : TimeSpan.Zero;

    /// <summary>
    /// Gets the compression ratio (SourceSize / ArchiveSize).
    /// </summary>
    public double CompressionRatio => ArchiveSize > 0 && SourceSize > 0
        ? (double)SourceSize / ArchiveSize
        : 0;

    /// <summary>
    /// Gets the compression percentage (1 - ArchiveSize/SourceSize) * 100.
    /// </summary>
    public double CompressionPercentage => ArchiveSize > 0 && SourceSize > 0
        ? (1 - (double)ArchiveSize / SourceSize) * 100
        : 0;

    /// <summary>
    /// Creates a new result with the given error message and Failed status.
    /// </summary>
    /// <param name="errorMessage">Error message.</param>
    /// <returns>A new result with the error message and Failed status.</returns>
    public CompressionResult WithError(string errorMessage) => this with
    {
        Status = CompressionStatus.Failed,
        ErrorMessage = errorMessage,
        EndTime = DateTimeOffset.Now
    };

    /// <summary>
    /// Creates a new result with the successful status.
    /// </summary>
    /// <param name="archivePath">Path to the archive.</param>
    /// <param name="archiveSize">Size of the archive.</param>
    /// <param name="verificationPassed">Whether verification passed.</param>
    /// <returns>A new result with the successful status.</returns>
    public CompressionResult WithSuccess(string archivePath, long archiveSize, bool verificationPassed) => this with
    {
        Status = CompressionStatus.Success,
        ArchivePath = archivePath,
        ArchiveSize = archiveSize,
        VerificationPassed = verificationPassed,
        EndTime = DateTimeOffset.Now
    };

    /// <summary>
    /// Creates a new result with the specified source deletion status.
    /// </summary>
    /// <param name="sourceDeleted">Whether the source was deleted.</param>
    /// <returns>A new result with the updated source deletion status.</returns>
    public CompressionResult WithSourceDeleted(bool sourceDeleted) => this with
    {
        SourceDeleted = sourceDeleted
    };

    /// <summary>
    /// Creates a new result with a cancelled status.
    /// </summary>
    /// <returns>A new result with the cancelled status.</returns>
    public CompressionResult WithCancelled() => this with
    {
        Status = CompressionStatus.Cancelled,
        EndTime = DateTimeOffset.Now
    };
}

