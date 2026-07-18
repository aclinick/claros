using Google.Protobuf;
using Onnx;
using WindowsNaturalVoices.Internal;

namespace WindowsNaturalVoices.Tests;

public class StreamingOpRewriterTests
{
    private static ModelProto BuildStreamingModel()
    {
        var model = new ModelProto { IrVersion = 7 };
        model.OpsetImport.Add(new OperatorSetIdProto { Domain = string.Empty, Version = 13 });
        model.OpsetImport.Add(new OperatorSetIdProto { Domain = "test.customop", Version = 1 });

        var graph = new GraphProto { Name = "g" };

        var conv = new NodeProto { OpType = "StreamingConv", Domain = "test.customop" };
        conv.Input.Add("x");
        conv.Input.Add("w");
        conv.Input.Add("streaming_control");
        conv.Output.Add("y");
        conv.Attribute.Add(new AttributeProto
        {
            Name = "kernel_shape",
            Type = AttributeProto.Types.AttributeType.Int,
            I = 3,
        });
        graph.Node.Add(conv);

        var add = new NodeProto { OpType = "StreamingAdd", Domain = "test.customop" };
        add.Input.Add("y");
        add.Input.Add("streaming_control");
        add.Output.Add("z");
        graph.Node.Add(add);

        // A node the rewriter must leave untouched.
        var relu = new NodeProto { OpType = "Relu", Domain = string.Empty };
        relu.Input.Add("z");
        relu.Output.Add("out");
        graph.Node.Add(relu);

        graph.Input.Add(new ValueInfoProto { Name = "x" });
        graph.Input.Add(new ValueInfoProto { Name = "streaming_control" });
        model.Graph = graph;

        return model;
    }

    private static ModelProto RewriteRoundTrip(ModelProto model, out int rewritten)
    {
        var rewrittenBytes = StreamingOpRewriter.Rewrite(model.ToByteArray(), out rewritten);
        return ModelProto.Parser.ParseFrom(rewrittenBytes);
    }

    [Fact]
    public void Rewrite_RenamesStreamingOpsToStandardOps()
    {
        var result = RewriteRoundTrip(BuildStreamingModel(), out var count);

        Assert.Equal(2, count);
        Assert.Equal("Conv", result.Graph.Node[0].OpType);
        Assert.Equal("Add", result.Graph.Node[1].OpType);
        Assert.Equal("Relu", result.Graph.Node[2].OpType);
        Assert.All(result.Graph.Node, n => Assert.Equal(string.Empty, n.Domain));
    }

    [Fact]
    public void Rewrite_DropsStreamingControlInputsFromNodes()
    {
        var result = RewriteRoundTrip(BuildStreamingModel(), out _);

        Assert.DoesNotContain("streaming_control", result.Graph.Node[0].Input);
        Assert.DoesNotContain("streaming_control", result.Graph.Node[1].Input);
        Assert.Equal(new[] { "x", "w" }, result.Graph.Node[0].Input);
    }

    [Fact]
    public void Rewrite_DropsStreamingControlGraphInput()
    {
        var result = RewriteRoundTrip(BuildStreamingModel(), out _);

        Assert.DoesNotContain(result.Graph.Input, v => v.Name == "streaming_control");
        Assert.Contains(result.Graph.Input, v => v.Name == "x");
    }

    [Fact]
    public void Rewrite_PromotesScalarConvAttributesToIntLists()
    {
        var result = RewriteRoundTrip(BuildStreamingModel(), out _);

        var attr = result.Graph.Node[0].Attribute.Single(a => a.Name == "kernel_shape");
        Assert.Equal(AttributeProto.Types.AttributeType.Ints, attr.Type);
        Assert.Equal(new long[] { 3 }, attr.Ints);
    }

    [Fact]
    public void Rewrite_StripsCustomOpsetImports()
    {
        var result = RewriteRoundTrip(BuildStreamingModel(), out _);

        Assert.DoesNotContain(result.OpsetImport, o => o.Domain == "test.customop");
        Assert.Contains(result.OpsetImport, o => o.Domain == string.Empty);
    }

    [Fact]
    public void Rewrite_LeavesStandardOnlyModelUnchanged()
    {
        var model = new ModelProto { IrVersion = 7 };
        model.OpsetImport.Add(new OperatorSetIdProto { Domain = string.Empty, Version = 13 });
        var graph = new GraphProto { Name = "g" };
        var relu = new NodeProto { OpType = "Relu" };
        relu.Input.Add("x");
        relu.Output.Add("y");
        graph.Node.Add(relu);
        model.Graph = graph;

        var result = RewriteRoundTrip(model, out var count);

        Assert.Equal(0, count);
        Assert.Equal("Relu", result.Graph.Node[0].OpType);
    }
}
