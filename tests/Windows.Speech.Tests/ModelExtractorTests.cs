using System.Text;
using Windows.Speech.Internal;

namespace Windows.Speech.Tests;

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
        Assert.False(File.Exists(dst.Path), "No partial ONNX file should be left behind on failure.");
    }

    [Fact]
    public void ExtractOnnx_SkipsFalsePositivePattern_AndReturnsRealModel()
    {
        // A byte run that trips the 0x08 <ir> 0x12 heuristic but is not a
        // parseable ModelProto (its field-2 length claims far more bytes than
        // exist). The scanner must reject it and keep looking.
        var header = Encoding.ASCII.GetBytes(new string('L', 32));
        var falsePositive = new byte[] { 0x08, 0x02, 0x12, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0xFF, 0xFF };
        var realOnnx = new byte[] { 0x08, 0x07, 0x12, 0x02, 0x67, 0x00 };

        var all = new byte[header.Length + falsePositive.Length + realOnnx.Length];
        Buffer.BlockCopy(header, 0, all, 0, header.Length);
        Buffer.BlockCopy(falsePositive, 0, all, header.Length, falsePositive.Length);
        Buffer.BlockCopy(realOnnx, 0, all, header.Length + falsePositive.Length, realOnnx.Length);

        using var file = TempFile.WithBytes(all, ".bin");

        var extracted = ModelExtractor.ExtractOnnx(file.Path);

        Assert.Equal(realOnnx, extracted);
    }

    [Fact]
    public void ExtractOnnx_ThrowsWhenOnlyUnparseableCandidatesExist()
    {
        var header = Enumerable.Repeat((byte)0x4C, 32).ToArray();
        var falsePositive = new byte[] { 0x08, 0x02, 0x12, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F, 0xFF, 0xFF };
        var all = header.Concat(falsePositive).ToArray();
        using var file = TempFile.WithBytes(all, ".bin");

        Assert.Throws<InvalidDataException>(() => ModelExtractor.ExtractOnnx(file.Path));
    }
}
