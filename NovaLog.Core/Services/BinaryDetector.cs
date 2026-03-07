namespace NovaLog.Core.Services;

/// <summary>
/// Quick heuristic to detect binary/compressed files by inspecting the first 1KB.
/// Prevents the indexer from wasting time on non-text files.
/// </summary>
public static class BinaryDetector
{
    private const int SampleSize = 1024;
    private const double NulThreshold = 0.01; // >1% NUL bytes = binary

    /// <summary>
    /// Returns true if the file appears to be binary (not a text log file).
    /// Checks for magic numbers (gzip, zip, PDF, ELF, PE) and NUL byte ratio.
    /// </summary>
    public static bool IsBinary(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int toRead = (int)Math.Min(SampleSize, fs.Length);
            if (toRead == 0) return false; // empty file is not binary

            byte[] buffer = new byte[toRead];
            int read = fs.Read(buffer, 0, toRead);
            var sample = buffer.AsSpan(0, read);

            if (HasMagicNumber(sample)) return true;

            int nulCount = 0;
            foreach (byte b in sample)
                if (b == 0) nulCount++;

            return (double)nulCount / read > NulThreshold;
        }
        catch (IOException) { return false; }
    }

    private static bool HasMagicNumber(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) return false;

        // Gzip: 1F 8B
        if (data[0] == 0x1F && data[1] == 0x8B) return true;
        // ZIP/DOCX/XLSX: 50 4B 03 04
        if (data[0] == 0x50 && data[1] == 0x4B && data[2] == 0x03 && data[3] == 0x04) return true;
        // PDF: %PDF
        if (data[0] == 0x25 && data[1] == 0x50 && data[2] == 0x44 && data[3] == 0x46) return true;
        // ELF: 7F 45 4C 46
        if (data[0] == 0x7F && data[1] == 0x45 && data[2] == 0x4C && data[3] == 0x46) return true;
        // PE (MZ): 4D 5A
        if (data[0] == 0x4D && data[1] == 0x5A) return true;

        return false;
    }
}
