namespace Folder_Compressor_Batcher.Exceptions;

/// <summary>
/// Exception thrown when a compression operation fails.
/// </summary>
[Serializable]
public class CompressionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionException"/> class.
    /// </summary>
    public CompressionException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CompressionException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="CompressionException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public CompressionException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when archive verification fails.
/// </summary>
[Serializable]
public sealed class ArchiveVerificationException : CompressionException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveVerificationException"/> class.
    /// </summary>
    public ArchiveVerificationException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveVerificationException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ArchiveVerificationException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveVerificationException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ArchiveVerificationException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a 7-Zip process execution fails.
/// </summary>
[Serializable]
public sealed class SevenZipException : CompressionException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SevenZipException"/> class.
    /// </summary>
    public SevenZipException() { }

    /// <summary>
    /// Gets the exit code of the 7-Zip process.
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// Gets the standard error output of the 7-Zip process.
    /// </summary>
    public string? StandardError { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SevenZipException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="exitCode">The exit code of the 7-Zip process.</param>
    /// <param name="standardError">The standard error output of the 7-Zip process.</param>
    public SevenZipException(string message, int? exitCode = null, string? standardError = null)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SevenZipException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    /// <param name="exitCode">The exit code of the 7-Zip process.</param>
    /// <param name="standardError">The standard error output of the 7-Zip process.</param>
    public SevenZipException(string message, Exception innerException, int? exitCode = null, string? standardError = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        StandardError = standardError;
    }
}

