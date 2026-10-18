namespace ClioReport.Expressions;

internal abstract class Node
{
    public abstract object? Eval(IDataContext ctx);
}

internal sealed class LiteralNode(object? value) : Node
{
    public override object? Eval(IDataContext ctx) => value;
}

internal sealed class FieldNode(string name) : Node
{
    public override object? Eval(IDataContext ctx)
    {
        if (!ctx.TryGetValue(name, out var v))
            throw new ExpressionException($"Unknown field '{name}'.");
        return Values.Normalize(v);
    }
}

internal sealed class UnaryNode(char op, Node operand) : Node
{
    public override object? Eval(IDataContext ctx)
    {
        var v = operand.Eval(ctx);
        if (op == '!') return !Values.Truthy(v);
        if (v is null) return null;
        return -Values.Num(v, "-");
    }
}

internal sealed class BinaryNode(string op, Node left, Node right) : Node
{
    public override object? Eval(IDataContext ctx)
    {
        var a = left.Eval(ctx);
        var b = right.Eval(ctx);
        switch (op)
        {
            case "+":
            case "-":
            case "*":
            case "/":
            case "%":
                return Values.Arith(op, a, b);
            case "==":
                return Values.Eq(a, b);
            case "!=":
                return !Values.Eq(a, b);
            default:
                if (a is null || b is null) return false;
                var c = Values.Cmp(a, b);
                return op switch { "<" => c < 0, "<=" => c <= 0, ">" => c > 0, _ => c >= 0 };
        }
    }
}

internal sealed class LogicalNode(bool isAnd, Node left, Node right) : Node
{
    public override object? Eval(IDataContext ctx) =>
        isAnd
            ? Values.Truthy(left.Eval(ctx)) && Values.Truthy(right.Eval(ctx))
            : Values.Truthy(left.Eval(ctx)) || Values.Truthy(right.Eval(ctx));
}

internal sealed class TernaryNode(Node cond, Node whenTrue, Node whenFalse) : Node
{
    public override object? Eval(IDataContext ctx) =>
        Values.Truthy(cond.Eval(ctx)) ? whenTrue.Eval(ctx) : whenFalse.Eval(ctx);
}

internal sealed class CallNode(FunctionInfo fn, Node[] args) : Node
{
    public override object? Eval(IDataContext ctx)
    {
        var values = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
            values[i] = args[i].Eval(ctx);
        return fn.Impl(values);
    }
}
