using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Folder_Compressor_Batcher.Models;

/// <summary>
/// Represents the progress of a compression operation.
/// </summary>
/// <param name="BytesProcessed">Number of bytes processed so far.</param>
/// <param name="TotalBytes">Total bytes to process.</param>
/// <param name="Speed">Speed in bytes per second.</param>
/// <param name="Threads">Number of threads being used.</param>
public sealed record CompressionProgress(
    long BytesProcessed,
    long TotalBytes,
    double Speed,
    int Threads)
{
    /// <summary>
    /// Gets the percentage complete.
    /// </summary>
    public double PercentComplete => TotalBytes > 0 
        ? (double)BytesProcessed / TotalBytes * 100 
        : 0;
}

