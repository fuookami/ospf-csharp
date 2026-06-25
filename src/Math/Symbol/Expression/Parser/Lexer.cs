#nullable enable

using System.Collections.Generic;
using System.Text;

namespace Fuookami.Ospf.Math.Symbol.Expression.Parser
{
    /// <summary>
    /// 词法分析器 / Lexer. Stateful scanner; sealed class (not record).
    /// 支持的关键字: and/or/not/in/is/like/contains/prefix/suffix/regex/exact/null/true/false
    /// </summary>
    public sealed class Lexer
    {
        private readonly string _input;
        private int _position;
        private char? _current;

        public Lexer(string input)
        {
            _input = input;
            _position = 0;
            _current = input.Length > 0 ? input[0] : null;
        }

        /// <summary>分析整个输入，返回词法单元列表 / Tokenize the entire input.</summary>
        public IReadOnlyList<Token> Tokenize()
        {
            var tokens = new List<Token>();
            var token = NextToken();
            while (token.Type != TokenType.Eof)
            {
                tokens.Add(token);
                token = NextToken();
            }
            tokens.Add(token);
            return tokens;
        }

        /// <summary>获取下一个词法单元 / Get the next token.</summary>
        public Token NextToken()
        {
            SkipWhitespace();

            if (_current is null)
                return Token.Eof(_position);

            var startPos = _position;

            // String literals
            if (_current == '\'' || _current == '"')
                return ReadString(startPos);

            // Number literals
            if (char.IsDigit(_current.Value) || (_current == '-' && PeekChar(1) is { } nc && char.IsDigit(nc)))
                return ReadNumber(startPos);

            // Identifiers and keywords
            if (char.IsLetter(_current.Value) || _current == '_')
                return ReadIdentifierOrKeyword(startPos);

            // Operators and symbols
            return _current switch
            {
                '(' => AdvanceReturn(new Token(TokenType.LParen, "(", startPos)),
                ')' => AdvanceReturn(new Token(TokenType.RParen, ")", startPos)),
                ',' => AdvanceReturn(new Token(TokenType.Comma, ",", startPos)),
                '=' => AdvanceReturn(new Token(TokenType.Eq, "=", startPos)),
                '!' => HandleExclamation(startPos),
                '<' => HandleLessThan(startPos),
                '>' => HandleGreaterThan(startPos),
                _ => AdvanceReturn(Token.Unknown(_current.Value.ToString(), startPos)),
            };
        }

        private Token AdvanceReturn(Token token)
        {
            Advance();
            return token;
        }

        private Token HandleExclamation(int startPos)
        {
            Advance();
            if (_current == '=')
                return AdvanceReturn(new Token(TokenType.Ne, "!=", startPos));
            return Token.Unknown("!", startPos);
        }

        private Token HandleLessThan(int startPos)
        {
            Advance();
            if (_current == '=')
                return AdvanceReturn(new Token(TokenType.Le, "<=", startPos));
            if (_current == '>')
                return AdvanceReturn(new Token(TokenType.Ne, "<>", startPos));
            return new Token(TokenType.Lt, "<", startPos);
        }

        private Token HandleGreaterThan(int startPos)
        {
            Advance();
            if (_current == '=')
                return AdvanceReturn(new Token(TokenType.Ge, ">=", startPos));
            return new Token(TokenType.Gt, ">", startPos);
        }

        private void SkipWhitespace()
        {
            while (_current is not null && char.IsWhiteSpace(_current.Value))
                Advance();
        }

        private void Advance()
        {
            _position++;
            _current = _position < _input.Length ? _input[_position] : null;
        }

        private char? PeekChar(int n = 1)
        {
            var idx = _position + n;
            return idx < _input.Length ? _input[idx] : null;
        }

        private Token ReadString(int startPos)
        {
            var quote = _current!.Value;
            Advance(); // Skip opening quote

            var sb = new StringBuilder();
            while (_current is not null && _current != quote)
            {
                if (_current == '\\')
                {
                    Advance();
                    switch (_current)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '\\': case '\'': case '"': sb.Append(_current); break;
                        default:
                            sb.Append('\\');
                            if (_current is not null) sb.Append(_current);
                            break;
                    }
                }
                else
                {
                    sb.Append(_current);
                }
                Advance();
            }

            if (_current == quote) Advance(); // Skip closing quote

            return new Token(TokenType.String, sb.ToString(), startPos);
        }

        private Token ReadNumber(int startPos)
        {
            var sb = new StringBuilder();

            if (_current == '-') { sb.Append(_current); Advance(); }

            while (_current is not null && char.IsDigit(_current.Value))
            {
                sb.Append(_current); Advance();
            }

            if (_current == '.' && PeekChar(1) is { } nc && char.IsDigit(nc))
            {
                sb.Append(_current); Advance();
                while (_current is not null && char.IsDigit(_current.Value))
                {
                    sb.Append(_current); Advance();
                }
            }

            if (_current is 'e' or 'E')
            {
                sb.Append(_current); Advance();
                if (_current is '+' or '-') { sb.Append(_current); Advance(); }
                while (_current is not null && char.IsDigit(_current.Value))
                {
                    sb.Append(_current); Advance();
                }
            }

            return new Token(TokenType.Number, sb.ToString(), startPos);
        }

        private Token ReadIdentifierOrKeyword(int startPos)
        {
            var sb = new StringBuilder();

            while (_current is not null && (char.IsLetterOrDigit(_current.Value) || _current == '_'))
            {
                sb.Append(_current); Advance();
            }

            // Check for dot-separated paths
            while (_current == '.' && PeekChar(1) is { } nc && (char.IsLetter(nc) || nc == '_'))
            {
                sb.Append(_current); Advance(); // Skip dot
                while (_current is not null && (char.IsLetterOrDigit(_current.Value) || _current == '_'))
                {
                    sb.Append(_current); Advance();
                }
            }

            var value = sb.ToString();
            return value.ToLowerInvariant() switch
            {
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
                _ => new Token(TokenType.Identifier, value, startPos),
            };
        }
    }

    /// <summary>词法分析扩展 / Lexer extensions.</summary>
    public static class LexerExtensions
    {
        /// <summary>字符串转词法单元列表 / String to token list.</summary>
        public static IReadOnlyList<Token> Tokenize(this string input) => new Lexer(input).Tokenize();
    }
}
