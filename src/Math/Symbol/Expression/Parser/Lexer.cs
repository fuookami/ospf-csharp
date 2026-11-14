#nullable enable

using System.Collections.Generic;
using System.Text;

namespace Fuookami.Ospf.Math.Symbol.Expression.Parser;
/// <summary>
/// 词法分析器 / Lexer. Stateful scanner; sealed class (not record).
/// 支持的关键字: and/or/not/in/is/like/contains/prefix/suffix/regex/exact/null/true/false
/// </summary>
public sealed class Lexer {
    private readonly string _input;
    private int _position;
    private char? _current;

    /// <summary>词法模式 / The lexing mode.</summary>
    /// <remarks>
    /// **这个开关不是风格问题，是 `-2^2` 读成什么的问题。**
    /// **布尔模式下 `-` 可作数字前缀**（`-1 &lt; x` 里那个负号是字面量的一部分），
    /// **标量模式下不行**——**否则 `-2^2` 的词法结果是 `-2`、`^`、`2`，一元负号就再没有机会比幂更松，
    /// 而 `-(2^2)` 与 `(-2)^2` 一个得 -4、一个得 4。**
    /// </remarks>
    public enum LexMode {
        /// <summary>布尔表达式 / Boolean expressions.</summary>
        Boolean,
        /// <summary>标量表达式 / Scalar expressions.</summary>
        Scalar,
    }

    private readonly LexMode _mode;

    public Lexer(string input, LexMode mode = LexMode.Boolean) {
        _input = input;
        _mode = mode;
        _position = 0;
        _current = input.Length > 0 ? input[0] : null;
    }

    /// <summary>分析整个输入，返回词法单元列表 / Tokenize the entire input.</summary>
    public IReadOnlyList<Token> Tokenize() {
        var tokens = new List<Token>();
        Token token = NextToken();
        while (token.Type != TokenType.Eof) {
            tokens.Add(token);
            token = NextToken();
        }
        tokens.Add(token);
        return tokens;
    }

    /// <summary>获取下一个词法单元 / Get the next token.</summary>
    public Token NextToken() {
        SkipWhitespace();

        if (_current is null) {
            return Token.Eof(_position);
        }

        int startPos = _position;

        // String literals
        if (_current == '\'' || _current == '"') {
            return ReadString(startPos);
        }

        // Number literals
        // **标量模式下只认数字开头**，见 `LexMode` 的注释。
        // **In scalar mode only a digit starts a number**; see the `LexMode` comment.
        bool startsNumber = char.IsDigit(_current.Value)
            || (_mode == LexMode.Boolean && _current == '-' && PeekChar(1) is { } nc && char.IsDigit(nc));
        if (startsNumber) {
            return ReadNumber(startPos);
        }

        // Identifiers and keywords
        if (char.IsLetter(_current.Value) || _current == '_') {
            return ReadIdentifierOrKeyword(startPos);
        }

        // Operators and symbols
        return _current switch {
            '(' => AdvanceReturn(new Token(TokenType.LParen, "(", startPos)),
            ')' => AdvanceReturn(new Token(TokenType.RParen, ")", startPos)),
            ',' => AdvanceReturn(new Token(TokenType.Comma, ",", startPos)),
            '=' => AdvanceReturn(new Token(TokenType.Eq, "=", startPos)),
            '!' => HandleExclamation(startPos),
            '<' => HandleLessThan(startPos),
            '>' => HandleGreaterThan(startPos),
            '+' => AdvanceReturn(new Token(TokenType.Plus, "+", startPos)),
            '-' => HandleMinus(startPos),
            '*' => HandleStar(startPos),
            '/' => AdvanceReturn(new Token(TokenType.Slash, "/", startPos)),
            '%' => AdvanceReturn(new Token(TokenType.Percent, "%", startPos)),
            '^' => AdvanceReturn(new Token(TokenType.Caret, "^", startPos)),
            '?' => AdvanceReturn(new Token(TokenType.Question, "?", startPos)),
            ':' => AdvanceReturn(new Token(TokenType.Colon, ":", startPos)),
            '&' => HandleAmpersand(startPos),
            '|' => HandlePipe(startPos),
            _ => AdvanceReturn(Token.Unknown(_current.Value.ToString(), startPos)),
        };
    }

    private Token AdvanceReturn(Token token) {
        Advance();
        return token;
    }

    /// <summary>减号：紧贴数字时已由数字分支吃掉，走到这里是二元减 / Minus: next to a digit the number branch took it, so this is binary minus.</summary>
    private Token HandleMinus(int startPos) => AdvanceReturn(new Token(TokenType.Minus, "-", startPos));

    /// <summary>星号，连着第二个星号是 `**` / Star, and a second star makes `**`.</summary>
    /// <remarks>
    /// **`**` 必须整体成一个单元**：拆成两个 `Star` 之后，`2**3` 与 `2* *3` 在文法上就没区别了，
    /// **而幂和连乘不是一回事。**
    /// </remarks>
    private Token HandleStar(int startPos) {
        Advance();
        if (_current == '*') {
            return AdvanceReturn(new Token(TokenType.DoubleStar, "**", startPos));
        }
        return new Token(TokenType.Star, "*", startPos);
    }

    /// <summary>`&amp;&amp;` / Logical and.</summary>
    /// <remarks>
    /// **单个 `&amp;` 是 `Unknown`**：这门语言里没有按位与，**认下来会让它走到文法层才被拒，那时的报错位置已经不是真位置了。**
    /// </remarks>
    private Token HandleAmpersand(int startPos) {
        Advance();
        if (_current == '&') {
            return AdvanceReturn(new Token(TokenType.AmpersandAmpersand, "&&", startPos));
        }
        return Token.Unknown("&", startPos);
    }

    /// <summary>`||` / Logical or.</summary>
    private Token HandlePipe(int startPos) {
        Advance();
        if (_current == '|') {
            return AdvanceReturn(new Token(TokenType.PipePipe, "||", startPos));
        }
        return Token.Unknown("|", startPos);
    }

    private Token HandleExclamation(int startPos) {
        Advance();
        if (_current == '=') {
            return AdvanceReturn(new Token(TokenType.Ne, "!=", startPos));
        }

        return Token.Unknown("!", startPos);
    }

    private Token HandleLessThan(int startPos) {
        Advance();
        if (_current == '=') {
            return AdvanceReturn(new Token(TokenType.Le, "<=", startPos));
        }

        if (_current == '>') {
            return AdvanceReturn(new Token(TokenType.Ne, "<>", startPos));
        }

        return new Token(TokenType.Lt, "<", startPos);
    }

    private Token HandleGreaterThan(int startPos) {
        Advance();
        if (_current == '=') {
            return AdvanceReturn(new Token(TokenType.Ge, ">=", startPos));
        }

        return new Token(TokenType.Gt, ">", startPos);
    }

    private void SkipWhitespace() {
        while (_current is not null && char.IsWhiteSpace(_current.Value)) {
            Advance();
        }
    }

    private void Advance() {
        _position++;
        _current = _position < _input.Length ? _input[_position] : null;
    }

    private char? PeekChar(int n = 1) {
        int idx = _position + n;
        return idx < _input.Length ? _input[idx] : null;
    }

    private Token ReadString(int startPos) {
        char quote = _current!.Value;
        Advance(); // Skip opening quote

        var sb = new StringBuilder();
        while (_current is not null && _current != quote) {
            if (_current == '\\') {
                Advance();
                switch (_current) {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '\\': case '\'': case '"': sb.Append(_current); break;
                    default:
                        sb.Append('\\');
                        if (_current is not null) {
                            sb.Append(_current);
                        }

                        break;
                }
            }
            else {
                sb.Append(_current);
            }
            Advance();
        }

        if (_current == quote) {
            Advance(); // Skip closing quote
        }

        return new Token(TokenType.String, sb.ToString(), startPos);
    }

    private Token ReadNumber(int startPos) {
        var sb = new StringBuilder();

        if (_current == '-') { sb.Append(_current); Advance(); }

        while (_current is not null && char.IsDigit(_current.Value)) {
            sb.Append(_current); Advance();
        }

        if (_current == '.' && PeekChar(1) is { } nc && char.IsDigit(nc)) {
            sb.Append(_current); Advance();
            while (_current is not null && char.IsDigit(_current.Value)) {
                sb.Append(_current); Advance();
            }
        }

        if (_current is 'e' or 'E') {
            sb.Append(_current); Advance();
            if (_current is '+' or '-') { sb.Append(_current); Advance(); }
            while (_current is not null && char.IsDigit(_current.Value)) {
                sb.Append(_current); Advance();
            }
        }

        return new Token(TokenType.Number, sb.ToString(), startPos);
    }

    private Token ReadIdentifierOrKeyword(int startPos) {
        var sb = new StringBuilder();

        while (_current is not null && (char.IsLetterOrDigit(_current.Value) || _current == '_')) {
            sb.Append(_current); Advance();
        }

        // Check for dot-separated paths
        while (_current == '.' && PeekChar(1) is { } nc && (char.IsLetter(nc) || nc == '_')) {
            sb.Append(_current); Advance(); // Skip dot
            while (_current is not null && (char.IsLetterOrDigit(_current.Value) || _current == '_')) {
                sb.Append(_current); Advance();
            }
        }

        string value = sb.ToString();
        return value.ToLowerInvariant() switch {
            "and" => new Token(TokenType.And, value, startPos),
            "or" => new Token(TokenType.Or, value, startPos),
            "not" => new Token(TokenType.Not, value, startPos),
            "in" => new Token(TokenType.In, value, startPos),
            "is" => new Token(TokenType.Is, value, startPos),
            "like" => new Token(TokenType.Like, value, startPos),
            "contains" => new Token(TokenType.Contains, value, startPos),
            "prefix" => new Token(TokenType.Prefix, value, startPos),
            "suffix" => new Token(TokenType.Suffix, value, startPos),
            "regex" or "matches" => new Token(TokenType.Regex, value, startPos),
            "exact" => new Token(TokenType.Exact, value, startPos),
            "null" => new Token(TokenType.Null, value, startPos),
            "true" => new Token(TokenType.True, value, startPos),
            "false" => new Token(TokenType.False, value, startPos),
            // **四个条件关键字在基线里就在关键字表里，C# 侧此前漏了；补上之后 `if` 不能再当变量名。**
            // **The four conditional keywords sit in the baseline keyword table; C# had missed them, and adding them means `if` can no longer be a variable name.**
            "if" => new Token(TokenType.If, value, startPos),
            "then" => new Token(TokenType.Then, value, startPos),
            "else" => new Token(TokenType.Else, value, startPos),
            "fi" => new Token(TokenType.Fi, value, startPos),
            _ => new Token(TokenType.Identifier, value, startPos),
        };
    }
}

/// <summary>词法分析扩展 / Lexer extensions.</summary>
public static class LexerExtensions {
    /// <summary>字符串转词法单元列表 / String to token list.</summary>
    public static IReadOnlyList<Token> Tokenize(this string input) => new Lexer(input).Tokenize();

    /// <summary>按标量模式切词 / Tokenise in scalar mode.</summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <returns>词法单元 / The tokens.</returns>
    public static IReadOnlyList<Token> TokenizeAsScalar(this string input) =>
        new Lexer(input, Lexer.LexMode.Scalar).Tokenize();
}
