using StardewValley;
using StardewValley.Delegates;
using StardewValley.Extensions;
using System;
using System.Collections.Generic;

using Log = ichortower.TowerCore.Log;
using Main = ichortower.TowerCore.Main;
using SEvent = StardewValley.Event;

namespace ichortower.ECC;

internal class Variable
{

    public static void command_VariableSet(SEvent evt, string[] args, EventContext context)
    {
        string err;
        if (!ArgUtility.TryGet(args, 1, out string varName, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        if (args.Length < 3) {
            context.LogErrorAndSkip($"no expression found after variable name");
            return;
        }
        ExprSyntaxTree ast = new();
        if (!ast.EvalString(string.Join(" ", args[2..]), out string res, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        VarDict[varName] = res;
        ++evt.CurrentCommand;
    }

    internal static Dictionary<string, string> VarDict = new();

    //variableset myvar 6
    //variableset myvar othervar + 1



    public static bool GSQ_VariableQuery(string[] query, GameStateQueryContext context)
    {
        return false;
    }

}

internal class ExprSyntaxTree
{
    public ExprSyntaxNode Root = null;
    public int Count { get; set; }

    public bool EvalString(string expr, out string ret, out string err)
    {
        if (!Parse(expr, out err)) {
            ret = null;
            return false;
        }
        if (Root is null) {
            ret = null;
            err = "no tree to eval";
            return false;
        }
        return Root.Eval(out ret, out err);
    }

    internal bool Parse(string expr, out string err)
    {
        err = "parse: not yet implemented";
        return false;
    }

    internal static Dictionary<string, ExprOperator> OpDict = new() {
        { "(",  ExprOperator.OpenParen },
        { ")",  ExprOperator.CloseParen },
        { "^",  ExprOperator.Exponent },
        { "*",  ExprOperator.Multiply },
        { "/",  ExprOperator.Divide },
        { "+",  ExprOperator.Add },
        { "-",  ExprOperator.Subtract },
        { "=",  ExprOperator.Equal },
        { "!=", ExprOperator.NotEqual },
        { "<",  ExprOperator.LessThan },
        { ">",  ExprOperator.GreaterThan },
        { "<=", ExprOperator.LessEqual },
        { ">=", ExprOperator.GreaterEqual },
        { ".",  ExprOperator.Concat },
    };

    internal class ExprSyntaxNode
    {
        internal string Value = null;
        internal ExprOperator Operator = ExprOperator.None;
        internal ExprSyntaxNode Lhs = null;
        internal ExprSyntaxNode Rhs = null;

        internal static bool Parse(string input, out ExprSyntaxNode res, out string err)
        {
            res = null;
            if (!Tokenize(input, out ExprToken[] tokens, out err)) {
                return false;
            }
            return true;
        }


        internal static bool Tokenize(string input, out ExprToken[] tokens, out string err)
        {
            err = null;
            tokens = null;
            List<ExprToken> cons = new();
            for (int i = 0; i < input.Length; ++i) {
                char c = input[i];
                if (char.IsWhiteSpace(c)) {
                    continue;
                }
                if (c == '"' || c == '\'') {
                    if (!GetStringLiteral(input, i, out string val, out err)) {
                        return false;
                    }
                    cons.Add(new ExprToken() {
                        Type = ExprTokenType.StringLiteral,
                        Value = val,
                    });
                    i += val.Length + 1;
                }
                else if (char.IsLetterOrDigit(c)) {
                    if (!GetIntOrIdentifier(input, i, out string val, out ExprTokenType type,
                                            out err)) {
                        return false;
                    }
                    cons.Add(new ExprToken() {
                        Type = type,
                        Value = val,
                    });
                    i += val.Length - 1;
                }
                else {
                    if (!GetOperator(input, i, out string val, out err)) {
                        return false;
                    }
                    cons.Add(new ExprToken() {
                        Type = ExprTokenType.Operator,
                        Value = val,
                    });
                    i += val.Length - 1;
                }
            }
            tokens = cons.ToArray();
            return true;
        }

        internal static bool GetStringLiteral(string input, int index, out string value, out string err)
        {
            value = null;
            err = null;
            int here = index + 1;
            char c = input[index];
            while (here < input.Length && input[here] != c) {
                ++here;
            }
            if (here >= input.Length) {
                err = $"Unterminated string literal ({c}, index {index})";
                return false;
            }
            value = input[(index+1)..here];
            Log.Debug($"string literal: '{value}'");
            return true;
        }

        internal static bool GetOperator(string input, int index, out string value, out string err)
        {
            value = null;
            err = null;
            int here = index + 1;
            while (here < input.Length) {
                char c = input[here];
                if (char.IsWhiteSpace(c) || char.IsLetterOrDigit(c)) {
                    break;
                }
                ++here;
            }
            value = input[index..here];
            Log.Debug($"operator: '{value}'");
            return true;
        }

        internal static bool GetIntOrIdentifier(string input, int index, out string value,
                out ExprTokenType type, out string err)
        {
            value = null;
            type = ExprTokenType.None;
            err = null;
            int here = index;
            bool allDigits = true;
            while (here < input.Length) {
                char c = input[here];
                if (!char.IsLetterOrDigit(c)) {
                    break;
                }
                else if (char.IsLetter(c)) {
                    allDigits = false;
                }
                ++here;
            }
            value = input[index..here];
            type = (allDigits ? ExprTokenType.IntLiteral : ExprTokenType.Identifier);
            Log.Debug($"{type.ToString()}: '{value}'");
            return true;
        }

        internal bool Eval(out string ret, out string err)
        {
            ret = null;
            string lValue = null;
            string rValue = null;
            if (Lhs?.Eval(out lValue, out err) is false) {
                return false;
            }
            if (Rhs?.Eval(out rValue, out err) is false) {
                return false;
            }
            int l = 0;
            int r = 0;
            // first switch is just to commonify the int parsing when using a command that needs ints
            switch (this.Operator) {
            case ExprOperator.Exponent:
            case ExprOperator.Multiply:
            case ExprOperator.Divide:
            case ExprOperator.Add:
            case ExprOperator.Subtract:
            case ExprOperator.LessThan:
            case ExprOperator.GreaterThan:
            case ExprOperator.LessEqual:
            case ExprOperator.GreaterEqual:
                if (!int.TryParse(lValue, out l)) {
                    err = $"could not parse '{lValue}' as an integer";
                    return false;
                }
                if (!int.TryParse(rValue, out r)) {
                    err = $"could not parse '{rValue}' as an integer";
                    return false;
                }
                break;
            }

            // actual behavior is here
            switch (this.Operator) {
            case ExprOperator.Exponent:
                ret = $"{(int)Math.Pow(l, r)}";
                break;
            case ExprOperator.Multiply:
                ret = $"{l * r}";
                break;
            case ExprOperator.Divide:
                ret = $"{l / r}";
                break;
            case ExprOperator.Add:
                ret = $"{l + r}";
                break;
            case ExprOperator.Subtract:
                ret = $"{l - r}";
                break;
            case ExprOperator.LessThan:
                ret = (l < r ? "true" : "false");
                break;
            case ExprOperator.GreaterThan:
                ret = (l > r ? "true" : "false");
                break;
            case ExprOperator.LessEqual:
                ret = (l <= r ? "true" : "false");
                break;
            case ExprOperator.GreaterEqual:
                ret = (l >= r ? "true" : "false");
                break;
            case ExprOperator.Equal:
                ret = (lValue.EqualsIgnoreCase(rValue) ? "true" : "false");
                break;
            case ExprOperator.NotEqual:
                ret = (lValue.EqualsIgnoreCase(rValue) ? "false" : "true");
                break;
            case ExprOperator.Concat:
                ret = lValue + rValue;
                break;
            case ExprOperator.Identifier:
                if (!Variable.VarDict.TryGetValue(this.Value, out ret)) {
                    err = $"unknown identifier '{this.Value}'";
                    return false;
                }
                break;
            case ExprOperator.None:
            default:
                ret = this.Value ?? "0";
                break;
            }
            err = null;
            return true;
        }

        internal bool AsBool() {
            if (Value is null ||
                    Value.EqualsIgnoreCase("false") ||
                    Value.EqualsIgnoreCase("0")) {
                return false;
            }
            return true;
        }
    }

    internal class ExprToken
    {
        internal ExprTokenType Type = ExprTokenType.None;
        internal string Value = null;
    }

    internal enum ExprTokenType
    {
        None,
        Identifier,
        Operator,
        StringLiteral,
        IntLiteral,
    }

    internal enum ExprOperator
    {
        None,
        Identifier,   // varName
        OpenParen,    // (
        CloseParen,   // )
        Exponent,     // ^
        Multiply,     // *
        Divide,       // /
        Add,          // +
        Subtract,     // -
        Equal,        // =
        NotEqual,     // !=
        LessThan,     // <
        GreaterThan,  // >
        LessEqual,    // <=
        GreaterEqual, // >=
        Concat,       // .
    }

}
