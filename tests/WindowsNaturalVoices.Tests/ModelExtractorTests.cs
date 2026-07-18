using System.Text;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class ModelExtractorTests
{
    // A plausible plaintext EULA header (never contains 0x08) followed by the
    // 8-byte hex tag, then a minimal ONNX ModelProto whose first field is the
    // ir_version varint (tag 0x08) followed by a Graph field (tag 0x12).
    private static byte[] BuildBinary(int headerLength, out int expectedOffset, out byte[] onnxPayload)
    {
        var header = Encoding.ASCII.GetBytes(new string('L', headerLength));
        var tag = Enumerable.Repeat((byte)0xFF, 8).ToArray();
        onnxPayload = new byte[] { 0x08, 0x07, 0x12, 0x02, 0x67, 0x00 };

        var all = new byte[header.Length + tag.Length + onnxPayload.Length];
        Buffer.BlockCopy(header, 0, all, 0, header.Length);
        Buffer.BlockCopy(tag, 0, all, header.Length, tag.Length);
        Buffer.BlockCopy(onnxPayload, 0, all, header.Length + tag.Length, onnxPayload.Length);

        expectedOffset = header.Length + tag.Length;
        return all;
    }

    [Fact]
    public void ExtractOnnx_StripsHeaderAndReturnsPayload()
    {
        var binary = BuildBinary(705, out _, out var payload);
        using var file = TempFile.WithBytes(binary, ".bin");

        var extracted = ModelExtractor.ExtractOnnx(file.Path);

        Assert.Equal(payload, extracted);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(705)]
    [InlineData(1200)]
    public void ExtractOnnx_DetectsOffsetDynamically_RegardlessOfHeaderLength(int headerLength)
    {
        var binary = BuildBinary(headerLength, out _, out var payload);
        using var file = TempFile.WithBytes(binary, ".bin");

        var extracted = ModelExtractor.ExtractOnnx(file.Path);

        Assert.Equal(payload, extracted);
    }

    [Fact]
    public void ExtractOnnx_ThrowsWhenNoOnnxHeaderPresent()
    {
        var noHeader = Enumerable.Repeat((byte)0x00, 2048).ToArray();
        using var file = TempFile.WithBytes(noHeader, ".bin");

        Assert.Throws<InvalidDataException>(() => ModelExtractor.ExtractOnnx(file.Path));
    }

    [Fact]
    public void ExtractOnnxToFile_WritesOnlyThePayload()
    {
        var binary = BuildBinary(705, out _, out var payload);
        using var src = TempFile.WithBytes(binary, ".bin");
        using var dst = TempFile.Create(".onnx");

        ModelExtractor.ExtractOnnxToFile(src.Path, dst.Path);

        Assert.Equal(payload, File.ReadAllBytes(dst.Path));
    }

    [Fact]
    public void ExtractOnnxToFile_ThrowsWhenNoOnnxHeaderPresent()
    {
        var noHeader = Enumerable.Repeat((byte)0x00, 2048).ToArray();
        using var src = TempFile.WithBytes(noHeader, ".bin");
        using var dst = TempFile.Create(".onnx");

        Assert.Throws<InvalidDataException>(() => ModelExtractor.ExtractOnnxToFile(src.Path, dst.Path));
    }
}
