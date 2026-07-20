namespace LeanKernel.Logic.Tools.BuiltIn;

/// <summary>
/// A minimal recursive-descent arithmetic evaluator for the <c>calculate</c> tool.
/// Supports +, -, *, /, parentheses, unary minus, and integer/decimal literals.
/// </summary>
internal static class ArithmeticEvaluator
{
    /// <summary>
    /// Evaluates a simple arithmetic expression string.
    /// </summary>
    public static double Evaluate(string expression)
    {
        var tokens = Tokenize(expression.AsSpan());
        var pos = 0;
        var result = ParseExpression(tokens, ref pos);
        if (pos != tokens.Count)
        {
            throw new FormatException($"Unexpected token at position {pos}: '{tokens[pos]}'");
        }

        return result;
    }

    private static double ParseExpression(IReadOnlyList<string> tokens, ref int pos)
    {
        var left = ParseTerm(tokens, ref pos);
        while (pos < tokens.Count && (tokens[pos] == "+" || tokens[pos] == "-"))
        {
            var op = tokens[pos++];
            var right = ParseTerm(tokens, ref pos);
            left = op == "+" ? left + right : left - right;
        }

        return left;
    }

    private static double ParseTerm(IReadOnlyList<string> tokens, ref int pos)
    {
        var left = ParseFactor(tokens, ref pos);
        while (pos < tokens.Count && (tokens[pos] == "*" || tokens[pos] == "/"))
        {
            var op = tokens[pos++];
            var right = ParseFactor(tokens, ref pos);
            if (op == "/" && right == 0)
            {
                throw new DivideByZeroException("Division by zero.");
            }

            left = op == "*" ? left * right : left / right;
        }

        return left;
    }

    private static double ParseFactor(IReadOnlyList<string> tokens, ref int pos)
    {
        if (pos >= tokens.Count)
        {
            throw new FormatException("Unexpected end of expression.");
        }

        if (tokens[pos] == "-")
        {
            pos++;
            return -ParseFactor(tokens, ref pos);
        }

        if (tokens[pos] == "(")
        {
            pos++;
            var val = ParseExpression(tokens, ref pos);
            if (pos >= tokens.Count || tokens[pos] != ")")
            {
                throw new FormatException("Missing closing parenthesis.");
            }

            pos++;
            return val;
        }

        if (double.TryParse(tokens[pos], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            pos++;
            return number;
        }

        throw new FormatException($"Unexpected token: '{tokens[pos]}'");
    }

    private static List<string> Tokenize(ReadOnlySpan<char> input)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                i++;
                continue;
            }

            if (input[i] is '+' or '-' or '*' or '/' or '(' or ')')
            {
                tokens.Add(input[i].ToString());
                i++;
                continue;
            }

            if (char.IsDigit(input[i]) || input[i] == '.')
            {
                var start = i;
                while (i < input.Length && (char.IsDigit(input[i]) || input[i] == '.'))
                {
                    i++;
                }

                tokens.Add(input[start..i].ToString());
                continue;
            }

            throw new FormatException($"Invalid character '{input[i]}' in expression.");
        }

        return tokens;
    }
}