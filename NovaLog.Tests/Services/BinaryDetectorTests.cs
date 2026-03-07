using NovaLog.Core.Services;

namespace NovaLog.Tests.Services;

public class BinaryDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public BinaryDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"novalog_bintest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateFile(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string CreateTextFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void IsBinary_TextFile_ReturnsFalse()
    {
        var path = CreateTextFile("plain.log",
            "2026-02-22 10:30:45.123 info: \tUser logged in\n" +
            "2026-02-22 10:30:46.456 debug: \tProcessing request\n");

        Assert.False(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_EmptyFile_ReturnsFalse()
    {
        var path = CreateFile("empty.log", []);
        Assert.False(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_GzipMagic_ReturnsTrue()
    {
        var data = new byte[100];
        data[0] = 0x1F;
        data[1] = 0x8B;
        var path = CreateFile("test.gz", data);

        Assert.True(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_ZipMagic_ReturnsTrue()
    {
        var data = new byte[100];
        data[0] = 0x50;
        data[1] = 0x4B;
        data[2] = 0x03;
        data[3] = 0x04;
        var path = CreateFile("test.zip", data);

        Assert.True(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_PdfMagic_ReturnsTrue()
    {
        var data = new byte[100];
        data[0] = 0x25;
        data[1] = 0x50;
        data[2] = 0x44;
        data[3] = 0x46;
        var path = CreateFile("test.pdf", data);

        Assert.True(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_PeMagic_ReturnsTrue()
    {
        var data = new byte[100];
        data[0] = 0x4D;
        data[1] = 0x5A;
        var path = CreateFile("test.exe", data);

        Assert.True(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_HighNulRatio_ReturnsTrue()
    {
        var data = new byte[200];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)'A';
        data[10] = 0;
        data[20] = 0;
        data[30] = 0;
        data[40] = 0;
        data[50] = 0;
        var path = CreateFile("binary.dat", data);

        Assert.True(BinaryDetector.IsBinary(path));
    }

    [Fact]
    public void IsBinary_LowNulRatio_ReturnsFalse()
    {
        var data = new byte[500];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)'X';
        data[250] = 0;
        var path = CreateFile("mostly_text.dat", data);

        Assert.False(BinaryDetector.IsBinary(path));
    }
}
