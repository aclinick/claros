namespace WindowsNaturalVoices.Internal;

/// <summary>
/// Strips the plaintext EULA header from a Natural Voice model binary.
///
/// Every <c>*.bin</c> in a voice package starts with a fixed 705-byte
/// license notice, followed by an 8-byte hex tag <c>ffffffff...</c>, and
/// then the raw ONNX ModelProto. Total prefix is 737 bytes on every voice
/// verified so far. The scanner walks the header looking for the first
/// ONNX ir_version protobuf tag (<c>0x08 &lt;small&gt; 0x12</c>) so this
/// class continues to work if Microsoft changes the header length.
/// </summary>
public static class ModelExtractor
{
    private const int MaxHeaderScan = 4096;

    /// <summary>Extract the raw ONNX bytes from a shipped model binary.</summary>
    public static byte[] ExtractOnnx(string modelBinPath)
    {
        var all = File.ReadAllBytes(modelBinPath);
        var offset = FindOnnxOffset(all)
            ?? throw new InvalidDataException(
                $"No ONNX header found in the first {MaxHeaderScan} bytes of {modelBinPath}.");
        var payload = new byte[all.Length - offset];
        Buffer.BlockCopy(all, offset, payload, 0, payload.Length);
        return payload;
    }

    /// <summary>Extract straight to a destination path, streaming.</summary>
    public static void ExtractOnnxToFile(string modelBinPath, string outOnnxPath)
    {
        using var src = File.OpenRead(modelBinPath);
        var scanBuf = new byte[MaxHeaderScan];
        var read = src.Read(scanBuf, 0, scanBuf.Length);
        var offset = FindOnnxOffset(scanBuf.AsSpan(0, read).ToArray())
            ?? throw new InvalidDataException(
                $"No ONNX header found in the first {read} bytes of {modelBinPath}.");

        Directory.CreateDirectory(Path.GetDirectoryName(outOnnxPath)!);
        using var dst = File.Create(outOnnxPath);
        dst.Write(scanBuf, offset, read - offset);
        src.CopyTo(dst);
    }

    private static int? FindOnnxOffset(byte[] bytes)
    {
        var upper = Math.Min(bytes.Length, MaxHeaderScan) - 4;
        for (var i = 0; i < upper; i++)
        {
            if (bytes[i] == 0x08 &&
                bytes[i + 1] >= 1 && bytes[i + 1] <= 15 &&
                bytes[i + 2] == 0x12)
            {
                return i;
            }
        }
        return null;
    }
}
