using System;
using System.Collections.Generic;
using System.Text;
using LinuxTerm.Core.Common;

namespace LinuxTerm.Core.Parser;

public static class Tokenizer
{
    private enum TokenType
    {
        Word,
        Pipe,        // |
        And,         // &&
        Or,          // ||
        Semicolon,   // ;
        RedirectOut, // >
        AppendOut,   // >>
        RedirectIn,  // <
        StderrToStdout // 2>&1
    }

    private record Token(TokenType Type, string Value);

    /// <summary>
    /// Parses a raw command string into an executable Shell AST.
    /// </summary>
    public static IShellNode? Parse(string input, ShellContext context)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        // First, expand aliases
        var expandedInput = ExpandAliases(input.Trim(), context);

        // Lex into tokens
        var tokens = Lex(expandedInput, context);
        if (tokens.Count == 0)
            return null;

        int index = 0;
        return ParseSequence(tokens, ref index);
    }

    private static string ExpandAliases(string input, ShellContext context)
    {
        // Simple alias expansion on command boundaries: start of line, after &&, ||, ;, |
        var parts = SplitCommandBoundaries(input);
        var result = new StringBuilder();

        for (int i = 0; i < parts.Count; i++)
        {
            var segment = parts[i];
            var trimmed = segment.TrimStart();
            if (!string.IsNullOrEmpty(trimmed))
            {
                int firstSpace = trimmed.IndexOf(' ');
                string cmd = firstSpace == -1 ? trimmed : trimmed[..firstSpace];
                if (context.Aliases.TryGetValue(cmd, out var aliasVal))
                {
                    string remainder = firstSpace == -1 ? string.Empty : trimmed[firstSpace..];
                    segment = aliasVal + remainder;
                }
            }
            result.Append(segment);
        }

        return result.ToString();
    }

    private static List<string> SplitCommandBoundaries(string input)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inSingle = false;
        bool inDouble = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
                sb.Append(c);
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
                sb.Append(c);
            }
            else if (!inSingle && !inDouble)
            {
                if (c == ';' || (c == '|' && i + 1 < input.Length && input[i + 1] == '|') ||
                    (c == '&' && i + 1 < input.Length && input[i + 1] == '&') || c == '|')
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                    if ((c == '|' && i + 1 < input.Length && input[i + 1] == '|') ||
                        (c == '&' && i + 1 < input.Length && input[i + 1] == '&'))
                    {
                        list.Add(input.Substring(i, 2));
                        i++;
                    }
                    else
                    {
                        list.Add(c.ToString());
                    }
                    continue;
                }
                sb.Append(c);
            }
            else
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
            list.Add(sb.ToString());

        return list;
    }

    private static List<Token> Lex(string input, ShellContext context)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Check operators
            if (c == ';')
            {
                tokens.Add(new Token(TokenType.Semicolon, ";"));
                i++;
                continue;
            }

            if (c == '&' && i + 1 < input.Length && input[i + 1] == '&')
            {
                tokens.Add(new Token(TokenType.And, "&&"));
                i += 2;
                continue;
            }

            if (c == '|' && i + 1 < input.Length && input[i + 1] == '|')
            {
                tokens.Add(new Token(TokenType.Or, "||"));
                i += 2;
                continue;
            }

            if (c == '|')
            {
                tokens.Add(new Token(TokenType.Pipe, "|"));
                i++;
                continue;
            }

            if (c == '2' && i + 3 < input.Length && input.Substring(i, 4) == "2>&1")
            {
                tokens.Add(new Token(TokenType.StderrToStdout, "2>&1"));
                i += 4;
                continue;
            }

            if (c == '>' && i + 1 < input.Length && input[i + 1] == '>')
            {
                tokens.Add(new Token(TokenType.AppendOut, ">>"));
                i += 2;
                continue;
            }

            if (c == '>')
            {
                tokens.Add(new Token(TokenType.RedirectOut, ">"));
                i++;
                continue;
            }

            if (c == '<')
            {
                tokens.Add(new Token(TokenType.RedirectIn, "<"));
                i++;
                continue;
            }

            // Word token
            var word = ReadWord(input, ref i, context);
            tokens.Add(new Token(TokenType.Word, word));
        }

        return tokens;
    }

    private static string ReadWord(string input, ref int i, ShellContext context)
    {
        var sb = new StringBuilder();

        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c) || c == ';' || c == '|' || c == '&' || c == '<' || c == '>')
            {
                break;
            }

            if (c == '\'')
            {
                // Single quotes: raw, no escapes, no variable expansion
                i++;
                while (i < input.Length && input[i] != '\'')
                {
                    sb.Append(input[i]);
                    i++;
                }
                if (i < input.Length && input[i] == '\'')
                    i++; // skip closing quote
            }
            else if (c == '"')
            {
                // Double quotes: variable expansion and basic escapes
                i++;
                var innerSb = new StringBuilder();
                while (i < input.Length && input[i] != '"')
                {
                    if (input[i] == '\\' && i + 1 < input.Length)
                    {
                        char next = input[i + 1];
                        if (next is '"' or '\\' or '$' or '`')
                        {
                            innerSb.Append(next);
                            i += 2;
                            continue;
                        }
                        else if (next == 'n')
                        {
                            innerSb.Append('\n');
                            i += 2;
                            continue;
                        }
                        else if (next == 't')
                        {
                            innerSb.Append('\t');
                            i += 2;
                            continue;
                        }
                    }
                    innerSb.Append(input[i]);
                    i++;
                }
                if (i < input.Length && input[i] == '"')
                    i++; // skip closing quote

                sb.Append(context.ExpandVariables(innerSb.ToString()));
            }
            else if (c == '\\')
            {
                // Escaped character
                if (i + 1 < input.Length)
                {
                    sb.Append(input[i + 1]);
                    i += 2;
                }
                else
                {
                    sb.Append('\\');
                    i++;
                }
            }
            else if (c == '$')
            {
                // Variable expansion outside quotes
                int start = i;
                i++;
                if (i < input.Length && input[i] == '?')
                {
                    sb.Append(context.LastExitCode);
                    i++;
                }
                else if (i < input.Length && input[i] == '{')
                {
                    int closeIdx = input.IndexOf('}', i + 1);
                    if (closeIdx != -1)
                    {
                        var varName = input.Substring(i + 1, closeIdx - (i + 1));
                        sb.Append(context.EnvironmentVariables.GetValueOrDefault(varName, string.Empty));
                        i = closeIdx + 1;
                    }
                    else
                    {
                        sb.Append("${");
                        i++;
                    }
                }
                else
                {
                    int varStart = i;
                    while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                    {
                        i++;
                    }
                    if (i > varStart)
                    {
                        var varName = input[varStart..i];
                        sb.Append(context.EnvironmentVariables.GetValueOrDefault(varName, string.Empty));
                    }
                    else
                    {
                        sb.Append('$');
                    }
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    private static IShellNode? ParseSequence(List<Token> tokens, ref int index)
    {
        var left = ParsePipeline(tokens, ref index);
        if (left == null)
            return null;

        while (index < tokens.Count)
        {
            var opToken = tokens[index];
            SequenceOperator? op = opToken.Type switch
            {
                TokenType.Semicolon => SequenceOperator.Semicolon,
                TokenType.And => SequenceOperator.And,
                TokenType.Or => SequenceOperator.Or,
                _ => null
            };

            if (op == null)
                break;

            index++; // consume operator
            var right = ParsePipeline(tokens, ref index);
            if (right != null)
            {
                left = new SequenceNode(left, right, op.Value);
            }
        }

        return left;
    }

    private static IShellNode? ParsePipeline(List<Token> tokens, ref int index)
    {
        var commands = new List<CommandNode>();

        while (index < tokens.Count)
        {
            var cmd = ParseCommand(tokens, ref index);
            if (cmd != null && cmd.Arguments.Count > 0)
            {
                commands.Add(cmd);
            }

            if (index < tokens.Count && tokens[index].Type == TokenType.Pipe)
            {
                index++; // consume pipe
                continue;
            }

            break;
        }

        if (commands.Count == 0)
            return null;

        if (commands.Count == 1)
            return commands[0];

        return new PipelineNode(commands);
    }

    private static CommandNode? ParseCommand(List<Token> tokens, ref int index)
    {
        var args = new List<string>();
        var redirects = new List<RedirectNode>();

        while (index < tokens.Count)
        {
            var token = tokens[index];

            if (token.Type is TokenType.Pipe or TokenType.And or TokenType.Or or TokenType.Semicolon)
            {
                break;
            }

            if (token.Type == TokenType.Word)
            {
                args.Add(token.Value);
                index++;
            }
            else if (token.Type == TokenType.RedirectOut)
            {
                index++;
                if (index < tokens.Count && tokens[index].Type == TokenType.Word)
                {
                    redirects.Add(new RedirectNode(RedirectType.OutputOverwrite, tokens[index].Value));
                    index++;
                }
            }
            else if (token.Type == TokenType.AppendOut)
            {
                index++;
                if (index < tokens.Count && tokens[index].Type == TokenType.Word)
                {
                    redirects.Add(new RedirectNode(RedirectType.OutputAppend, tokens[index].Value));
                    index++;
                }
            }
            else if (token.Type == TokenType.RedirectIn)
            {
                index++;
                if (index < tokens.Count && tokens[index].Type == TokenType.Word)
                {
                    redirects.Add(new RedirectNode(RedirectType.Input, tokens[index].Value));
                    index++;
                }
            }
            else if (token.Type == TokenType.StderrToStdout)
            {
                redirects.Add(new RedirectNode(RedirectType.StderrToStdout, string.Empty));
                index++;
            }
            else
            {
                index++;
            }
        }

        return args.Count > 0 || redirects.Count > 0 ? new CommandNode(args, redirects) : null;
    }
}
