using Google.Protobuf;
using Onnx;

namespace Claros.Internal;

/// <summary>
/// Rewrites the "Streaming*" custom operators that Microsoft's Natural Voice
/// vocoder ships with so the model loads under stock ONNX Runtime.
///
/// The vocoder uses op types like <c>StreamingConv</c> in the
/// <c>test.customop</c> domain. Each is a thin wrapper around the standard
/// ONNX op of the matching name and adds a <c>streaming_control</c> input
/// that carries per-chunk state. Ignoring that state and switching each
/// wrapper back to its standard op produces valid ONNX that stock runtime
/// executes correctly for non-streaming batch inference.
/// </summary>
internal static class StreamingOpRewriter
{
    /// <summary>The custom operator domain the Natural Voice vocoder ships.</summary>
    private const string CustomDomain = "test.customop";

    private static readonly IReadOnlyDictionary<string, string> Renames = new Dictionary<string, string>
    {
        ["StreamingConv"] = "Conv",
        ["StreamingConvTranspose"] = "ConvTranspose",
        ["StreamingAdd"] = "Add",
        ["StreamingGRU"] = "GRU",
        ["StreamingLSTM"] = "LSTM",
        ["StreamingPad"] = "Pad",
    };

    private static readonly HashSet<string> ConvAttributes = new()
    {
        "dilations", "kernel_shape", "pads", "strides", "output_padding",
    };

    /// <summary>
    /// Rewrite a raw ONNX ModelProto byte payload in-place. Returns the number
    /// of nodes that were converted from the custom streaming domain to the
    /// standard ONNX operator set.
    /// </summary>
    public static byte[] Rewrite(byte[] onnxBytes, out int rewritten)
    {
        var model = ModelProto.Parser.ParseFrom(onnxBytes);
        rewritten = RewriteInPlace(model);
        return model.ToByteArray();
    }

    private static int RewriteInPlace(ModelProto model)
    {
        var count = 0;
        foreach (var node in model.Graph.Node)
        {
            // Only touch operators in the vocoder's custom domain. A standard
            // ONNX op that happens to share a name (for example a real "Add")
            // lives in the default domain and must be left alone.
            if (node.Domain != CustomDomain)
            {
                continue;
            }

            if (!Renames.TryGetValue(node.OpType, out var renamed))
            {
                // A custom-domain operator the library does not know how to
                // rewrite would silently break inference; fail loudly instead.
                throw new VoicePackageFormatException(
                    $"Vocoder uses unsupported custom operator '{node.OpType}' in domain '{CustomDomain}'.");
            }

            node.OpType = renamed;
            node.Domain = string.Empty;

            // Drop the streaming_control input; the standard ops don't take it.
            for (var i = node.Input.Count - 1; i >= 0; i--)
            {
                if (node.Input[i] == "streaming_control")
                {
                    node.Input.RemoveAt(i);
                }
            }

            // The custom ops declare scalar attributes for values that the
            // standard ops require as int lists. Promote them.
            foreach (var attr in node.Attribute)
            {
                if (!ConvAttributes.Contains(attr.Name)) continue;
                if (attr.Type == AttributeProto.Types.AttributeType.Int)
                {
                    var v = attr.I;
                    attr.Type = AttributeProto.Types.AttributeType.Ints;
                    attr.I = 0;
                    attr.Ints.Add(v);
                }
            }

            count++;
        }

        // Drop the streaming_control graph input to match the removed op inputs.
        for (var i = model.Graph.Input.Count - 1; i >= 0; i--)
        {
            if (model.Graph.Input[i].Name == "streaming_control")
            {
                model.Graph.Input.RemoveAt(i);
            }
        }

        // Strip only the custom opset import so ORT does not look for the
        // missing domain. Any other non-standard domains are left in place
        // for the runtime to resolve or reject on its own.
        for (var i = model.OpsetImport.Count - 1; i >= 0; i--)
        {
            if (model.OpsetImport[i].Domain == CustomDomain)
            {
                model.OpsetImport.RemoveAt(i);
            }
        }

        return count;
    }
}
