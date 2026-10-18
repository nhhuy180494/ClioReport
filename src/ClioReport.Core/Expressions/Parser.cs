using System.Globalization;
using System.Text;

namespace ClioReport.Expressions;

internal enum TokenType { Number, String, Ident, Op, LParen, RParen, Comma, End }

internal readonly record struct Token(TokenType Type, string Text, int Pos, decimal Number = 0m);

/// <summary>
/// Precedence (low to high): ?: , || , &amp;&amp; , == != , &lt; &lt;= &gt; &gt;= , + - , * / % , unary - !
/// </summary>
internal sealed class Parser
{
    private const int MaxDepth = 64;

    private readonly List<Token> _tokens;
    private int _pos;
    private int _depth;

    public Parser(string text) => _tokens = Tokenize(text);

    private Token Cur => _tokens[_pos];

    private static ExpressionException Err(string message, int pos) => new($"{message} at position {pos}.");

    public Node ParseAll()
    {
        var node = Ternary();
        if (Cur.Type != TokenType.End)
            throw Err($"Unexpected '{Cur.Text}'", Cur.Pos);
        return node;
    }

    // ---- parsing ----

    private void Enter()
    {
        if (++_depth > MaxDepth)
            throw Err("Expression is nested too deeply", Cur.Pos);
    }

    private bool IsOp(string op) => Cur.Type == TokenType.Op && Cur.Text == op;

    private Node Ternary()
    {
        Enter();
        try
        {
            var cond = Or();
            if (!IsOp("?")) return cond;
            _pos++;
            var whenTrue = Ternary();
            if (!IsOp(":")) throw Err("Expected ':'", Cur.Pos);
            _pos++;
            var whenFalse = Ternary();
            return new TernaryNode(cond, whenTrue, whenFalse);
        }
        finally
        {
            _depth--;
        }
    }

    private Node Or()
    {
        var left = And();
        while (IsOp("||"))
        {
            _pos++;
            left = new LogicalNode(false, left, And());
        }
        return left;
    }

    private Node And()
    {
        var left = Equality();
        while (IsOp("&&"))
        {
            _pos++;
            left = new LogicalNode(true, left, Equality());
        }
        return left;
    }

    private Node Equality() => Left(Relational, "==", "!=");

    private Node Relational() => Left(Additive, "<", "<=", ">", ">=");

    private Node Additive() => Left(Multiplicative, "+", "-");

    private Node Multiplicative() => Left(Unary, "*", "/", "%");

    private Node Left(Func<Node> next, params string[] ops)
    {
        var left = next();
        while (Cur.Type == TokenType.Op && Array.IndexOf(ops, Cur.Text) >= 0)
        {
            var op = Cur.Text;
            _pos++;
            left = new BinaryNode(op, left, next());
        }
        return left;
    }

    private Node Unary()
    {
        if (IsOp("-") || IsOp("!"))
        {
            Enter();
            try
            {
                var op = Cur.Text[0];
                _pos++;
                return new UnaryNode(op, Unary());
            }
            finally
            {
                _depth--;
            }
        }
        return Primary();
    }

    private Node Primary()
    {
        var t = Cur;
        switch (t.Type)
        {
            case TokenType.Number:
                _pos++;
                return new LiteralNode(t.Number);
            case TokenType.String:
                _pos++;
                return new LiteralNode(t.Text);
            case TokenType.LParen:
            {
                _pos++;
                var inner = Ternary();
                Expect(TokenType.RParen, ")");
                return inner;
            }
            case TokenType.Ident:
                _pos++;
                if (Cur.Type == TokenType.LParen) return Call(t);
                if (t.Text.Equals("true", StringComparison.OrdinalIgnoreCase)) return new LiteralNode(true);
                if (t.Text.Equals("false", StringComparison.OrdinalIgnoreCase)) return new LiteralNode(false);
                if (t.Text.Equals("null", StringComparison.OrdinalIgnoreCase)) return new LiteralNode(null);
                return new FieldNode(t.Text);
            default:
                throw Err(t.Type == TokenType.End ? "Unexpected end of expression" : $"Unexpected '{t.Text}'", t.Pos);
        }
    }

    private Node Call(Token name)
    {
        if (name.Text.Contains('.'))
            throw Err($"'{name.Text}' is not a callable function", name.Pos);
        if (!Functions.TryGet(name.Text, out var fn))
            throw Err($"Unknown function '{name.Text}'", name.Pos);

        _pos++; // '('
        var args = new List<Node>();
        if (Cur.Type != TokenType.RParen)
        {
            do
            {
                args.Add(Ternary());
            } while (Accept(TokenType.Comma));
        }
        Expect(TokenType.RParen, ")");

        if (args.Count < fn.MinArgs || args.Count > fn.MaxArgs)
            throw Err($"Function '{name.Text}' expects {fn.MinArgs}..{fn.MaxArgs} arguments", name.Pos);
        return new CallNode(fn, args.ToArray());
    }

    private bool Accept(TokenType type)
    {
        if (Cur.Type != type) return false;
        _pos++;
        return true;
    }

    private void Expect(TokenType type, string text)
    {
        if (!Accept(type)) throw Err($"Expected '{text}'", Cur.Pos);
    }

    // ---- tokenizer ----

    private static List<Token> Tokenize(string s)
    {
        var list = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                var start = i;
                while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    if (i >= s.Length || !char.IsAsciiDigit(s[i])) throw Err("Invalid number", start);
                    while (i < s.Length && char.IsAsciiDigit(s[i])) i++;
                }
                if (!decimal.TryParse(s.AsSpan(start, i - start), NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                    throw Err("Invalid number", start);
                list.Add(new Token(TokenType.Number, s[start..i], start, number));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
                var text = s[start..i];
                if (text.EndsWith('.') || text.Contains(".."))
                    throw Err("Invalid field name", start);
                list.Add(new Token(TokenType.Ident, text, start));
                continue;
            }

            if (c == '\'' || c == '"')
            {
                var start = i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Err("Unterminated string", start);
                    var ch = s[i++];
                    if (ch == c) break;
                    if (ch == '\\')
                    {
                        if (i >= s.Length) throw Err("Unterminated string", start);
                        var e = s[i++];
                        sb.Append(e switch { 'n' => '\n', 't' => '\t', _ => e });
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                }
                list.Add(new Token(TokenType.String, sb.ToString(), start));
                continue;
            }

            if (c == '(') { list.Add(new Token(TokenType.LParen, "(", i++)); continue; }
            if (c == ')') { list.Add(new Token(TokenType.RParen, ")", i++)); continue; }
            if (c == ',') { list.Add(new Token(TokenType.Comma, ",", i++)); continue; }

            string? op = null;
            if (i + 1 < s.Length)
            {
                var two = s.Substring(i, 2);
                if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||") op = two;
            }
            if (op is null && "+-*/%<>!?:".Contains(c)) op = c.ToString();
            if (op is null) throw Err($"Unexpected character '{c}'", i);

            list.Add(new Token(TokenType.Op, op, i));
            i += op.Length;
        }

        list.Add(new Token(TokenType.End, "", s.Length));
        return list;
    }
}
