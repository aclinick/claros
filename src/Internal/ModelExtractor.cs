using Google.Protobuf;
using Onnx;

namespace Windows.Speech.Internal;

/// <summary>
/// Strips the plaintext EULA header from a Natural Voice model binary.
///
/// Every <c>*.bin</c> in a voice package starts with a fixed 705-byte
/// license notice, followed by an 8-byte hex tag <c>ffffffff...</c>, and
/// then the raw ONNX ModelProto. Total prefix is 737 bytes on every voice
/// verified so far. The scanner walks the header looking for the first
/// ONNX ir_version protobuf tag (<c>0x08 &lt;small&gt; 0x12</c>) and confirms
/// each candidate by fully parsing it as a ModelProto, so a byte pattern that
/// merely resembles the tag inside the header cannot produce a bogus offset.
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
                $"No parseable ONNX model found in the first {MaxHeaderScan} bytes of {modelBinPath}.");
        var payload = new byte[all.Length - offset];
        Buffer.BlockCopy(all, offset, payload, 0, payload.Length);
        return payload;
    }

    /// <summary>Extract straight to a destination path.</summary>
    public static void ExtractOnnxToFile(string modelBinPath, string outOnnxPath)
    {
        var all = File.ReadAllBytes(modelBinPath);
        var offset = FindOnnxOffset(all)
            ?? throw new InvalidDataException(
                $"No parseable ONNX model found in the first {MaxHeaderScan} bytes of {modelBinPath}.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(outOnnxPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        try
        {
            using var dst = File.Create(outOnnxPath);
            dst.Write(all, offset, all.Length - offset);
        }
        catch
        {
            // Never leave a truncated ONNX file behind for a later load to trip over.
            TryDelete(outOnnxPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; surface the original failure instead.
        }
    }

    private static int? FindOnnxOffset(byte[] bytes)
    {
        var upper = Math.Min(bytes.Length, MaxHeaderScan) - 4;
        for (var i = 0; i < upper; i++)
        {
            if (bytes[i] != 0x08 ||
                bytes[i + 1] < 1 || bytes[i + 1] > 15 ||
                bytes[i + 2] != 0x12)
            {
                continue;
            }

            if (IsParseableModel(bytes, i))
            {
                return i;
            }
        }

        return null;
    }

    // Confirm a candidate offset really begins a ModelProto. A stray
    // 0x08/0x12 pattern in the license header parses as garbage (or an
    // out-of-range ir_version) and is rejected so scanning continues.
    private static bool IsParseableModel(byte[] bytes, int offset)
    {
        try
        {
            var model = ModelProto.Parser.ParseFrom(bytes.AsSpan(offset));
            return model.IrVersion is >= 1 and <= 15;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }
}
