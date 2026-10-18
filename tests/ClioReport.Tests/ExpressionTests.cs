using ClioReport.Expressions;
using Xunit;

namespace ClioReport.Tests;

public class ExpressionTests
{
    private static object? Eval(string expr, Dictionary<string, object?>? fields = null) =>
        Expression.Parse(expr).Evaluate(new DictionaryDataContext(fields));

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("10 % 4", 2)]
    [InlineData("-5 + 2", -3)]
    [InlineData("10 / 4 * 2", 5)]
    public void Arithmetic_FollowsPrecedence(string expr, int expected) =>
        Assert.Equal((decimal)expected, (decimal)Eval(expr)!);

    [Fact]
    public void Field_NumericTypesNormalizedToDecimal()
    {
        var result = Eval("item.qty * item.price",
            new() { ["item.qty"] = 3, ["item.price"] = 2.5 });

        Assert.Equal(7.5m, (decimal)result!);
    }

    [Fact]
    public void Plus_WithString_Concatenates() =>
        Assert.Equal("SL: 3", Eval("'SL: ' + qty", new() { ["qty"] = 3 }));

    [Fact]
    public void LogicAndTernary()
    {
        var fields = new Dictionary<string, object?> { ["qty"] = 3, ["kho"] = "B" };

        Assert.Equal("x", Eval("qty > 2 && !(kho == 'A') ? 'x' : 'y'", fields));
        Assert.Equal("y", Eval("qty > 5 || kho == 'A' ? 'x' : 'y'", fields));
    }

    [Fact]
    public void Null_PropagatesInArithmetic_AndIsNullSubstitutes()
    {
        var fields = new Dictionary<string, object?> { ["a"] = null };

        Assert.Null(Eval("a + 1", fields));
        Assert.Equal(1m, (decimal)Eval("IsNull(a, 0) + 1", fields)!);
    }

    [Fact]
    public void Functions_Work()
    {
        Assert.Equal(2.35m, (decimal)Eval("Round(2.345, 2)")!);
        Assert.Equal("ABC", Eval("Upper('abc')"));
        Assert.Equal("1,234.5", Eval("Format(1234.5, 'N1')"));
        Assert.Equal(3m, (decimal)Eval("Max(1, 3)")!);
    }

    [Theory]
    [InlineData("nope")]                  // unknown field
    [InlineData("1 / 0")]
    [InlineData("Unknown(1)")]
    [InlineData("Round()")]               // wrong arity
    [InlineData("1 +")]
    [InlineData("'abc")]
    [InlineData("1 = 1")]
    [InlineData("item.GetType()")]        // no member calls
    [InlineData("System.Environment.Exit(1)")]
    [InlineData("1 < 'a'")]               // type mismatch
    public void Invalid_Throws(string expr) =>
        Assert.Throws<ExpressionException>(() => Eval(expr));

    [Fact]
    public void DeepNesting_Throws()
    {
        var expr = new string('(', 100) + "1" + new string(')', 100);

        Assert.Throws<ExpressionException>(() => Eval(expr));
    }
}
