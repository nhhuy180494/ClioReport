using ClioReport.Expressions;
using ClioReport.Model;

namespace ClioReport;

internal sealed class AggregateState
{
    private readonly Expression _expression;
    private long _count;
    private decimal _sum;
    private decimal? _min;
    private decimal? _max;

    public AggregateState(AggregateDefinition definition, Expression expression)
    {
        Definition = definition;
        _expression = expression;
    }

    public AggregateDefinition Definition { get; }

    public void Reset()
    {
        _count = 0;
        _sum = 0;
        _min = null;
        _max = null;
    }

    /// <summary>Null values are skipped. Count counts non-null values; the others need numbers.</summary>
    public void Add(IDataContext ctx)
    {
        var v = _expression.Evaluate(ctx);
        if (v is null) return;

        if (Definition.Function == AggregateFunction.Count)
        {
            _count++;
            return;
        }

        var d = Values.Num(v, Definition.Function.ToString());
        _count++;
        _sum += d;
        _min = _min is null ? d : Math.Min(_min.Value, d);
        _max = _max is null ? d : Math.Max(_max.Value, d);
    }

    public object? Value
    {
        get
        {
            switch (Definition.Function)
            {
                case AggregateFunction.Count: return (decimal)_count;
                case AggregateFunction.Sum: return _sum;
                case AggregateFunction.Avg: return _count == 0 ? null : _sum / _count;
                case AggregateFunction.Min: return _min;
                case AggregateFunction.Max: return _max;
                default: throw new ReportException($"Unsupported aggregate function '{Definition.Function}'.");
            }
        }
    }
}
