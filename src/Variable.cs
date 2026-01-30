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

    public static void command_VarSet(SEvent evt, string[] args, EventContext context)
    {
        string err;
        if (!ArgUtility.TryGet(args, 1, out string varName, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        if (varName.StartsWithIgnoreCase("ECC")) {
            context.LogErrorAndSkip($"variable name must not start with 'ECC'");
            return;
        }
        if (args.Length < 3) {
            context.LogErrorAndSkip($"no expression found after variable name");
            return;
        }
        if (!ExprNode.EvalString(string.Join(" ", args[2..]), out string res, out err)) {
            context.LogErrorAndSkip(err);
            return;
        }
        VarDict[varName] = res;
        if (!CleanupQueued) {
            // register this to main event instead of evt, so cleanup will still fire
            // if this is used in a stream
            Game1.CurrentEvent.onEventFinished += CleanUp;
            CleanupQueued = true;
        }
        ++evt.CurrentCommand;
    }

    internal static Dictionary<string, string> VarDict = new();

    internal static bool CleanupQueued = false;

    internal static void CleanUp()
    {
        Log.Debug("running var cleanup");
        VarDict.Clear();
        CleanupQueued = false;
    }


    public static bool GSQ_VAR_QUERY(string[] query, GameStateQueryContext context)
    {
        string err = null;
        if (query.Length < 2) {
            err = "Requires input to parse";
            return GameStateQuery.Helpers.ErrorResult(query, err);
        }
        if (!ExprNode.EvalString(string.Join(" ", query[1..]), out string res, out err)) {
            return GameStateQuery.Helpers.ErrorResult(query, err);
        }
        if (res.EqualsIgnoreCase("false") || res == "0") {
            return false;
        }
        return true;
    }

}

internal class ExprNode
{
    internal ExprToken Token = null;
    internal ExprNode Lhs = null;
    internal ExprNode Rhs = null;
    internal int OverridePriority = -1;

    internal class ExprToken
    {
        internal ExprTokenType Type = ExprTokenType.None;
        internal ExprOperator Operator = ExprOperator.None;
        internal string Value = null;

        internal bool IsOperator(ExprOperator which) {
            return Type == ExprTokenType.Operator && Operator == which;
        }
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

    internal bool IsOperator {
        get {
            return this.Token.Type == ExprTokenType.Operator;
        }
    }

    internal bool IsValue {
        get {
            return this.Token.Type != ExprTokenType.Operator;
        }
    }

    internal bool ShouldCountAsValue {
        get {
            return this.IsValue || (this.Lhs is not null && this.Rhs is not null);
        }
    }

    internal static bool ExpectingValue(ExprNode n) {
        if (n is null) {
            return true;
        }
        while (n.Rhs is not null) {
            n = n.Rhs;
        }
        return n.IsOperator;
    }

    internal static bool ExpectingOperator(ExprNode n) {
        if (n is null) {
            return false;
        }
        while (n.Rhs is not null) {
            n = n.Rhs;
        }
        return n.IsValue;
    }

    internal static int PriorityOf(ExprNode n) {
        if (n.OverridePriority >= 0) {
            return n.OverridePriority;
        }
        return n.Token.Operator switch {
            ExprOperator.OpenParen => 5,
            ExprOperator.CloseParen => 5,
            ExprOperator.Exponent => 4,
            ExprOperator.Multiply => 3,
            ExprOperator.Divide => 3,
            ExprOperator.Add => 2,
            ExprOperator.Subtract => 2,
            _ => 0,
        };
    }

    internal static bool EvalString(string input, out string res, out string error)
    {
        res = null;
        ExprNode tree = new();
        if (!tree.Parse(input, out error)) {
            return false;
        }
        if (!tree.Eval(out res, out error)) {
            return false;
        }
        return true;
    }

    internal bool Eval(out string ret, out string err)
    {
        ret = null;
        err = null;
        // first, leaf node values
        switch (this.Token?.Type ?? ExprTokenType.None) {
        case ExprTokenType.Identifier:
            if (!Variable.VarDict.TryGetValue(this.Token.Value, out ret)) {
                err = $"unknown identifier '{this.Token.Value}'";
                return false;
            }
            return true;
        case ExprTokenType.StringLiteral:
            ret = this.Token.Value ?? "";
            return true;
        case ExprTokenType.IntLiteral:
            ret = this.Token.Value ?? "0";
            return true;
        case ExprTokenType.None:
            err = $"Invalid node with token type 'None'";
            return false;
        }
        // by elimination, this is a binary operator, so we need lhs and rhs now
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
        ExprOperator eop = (this.Token?.Operator ?? ExprOperator.None);
        // if this is an operation on ints, try to parse them
        switch (eop) {
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
        switch (eop) {
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
        case ExprOperator.None:
            err = "Tried to eval operator node with type 'None'";
            return false;
        default:
            ret = this.Token?.Value ?? "0";
            break;
        }
        err = null;
        return true;
    }

    internal bool Parse(string input, out string err)
    {
        if (!Tokenize(input, out ExprToken[] tokens, out err)) {
            return false;
        }
        if (!ParseArray(tokens, out ExprNode res, out err)) {
            return false;
        }
        this.Token = res.Token;
        this.Lhs = res.Lhs;
        this.Rhs = res.Rhs;
        return true;
    }

    internal static bool ParseArray(ExprToken[] input, out ExprNode res, out string err)
    {
        res = null;
        err = null;
        ExprNode root = null;
        for (int i = 0; i < input.Length; ++i) {
            ExprToken token = input[i];
            ExprNode current = null;
            if (token.Type == ExprTokenType.None) {
                err = $"Found invalid token type 'None'";
                return false;
            }
            if (token.IsOperator(ExprOperator.CloseParen)) {
                err = $"Unmatched parenthesis ')' (token {i})";
                return false;
            }
            if (token.IsOperator(ExprOperator.OpenParen)) {
                int depth = 1;
                int j = i + 1;
                for (; j < input.Length; ++j) {
                    if (input[j].IsOperator(ExprOperator.OpenParen)) {
                        ++depth;
                    }
                    else if (input[j].IsOperator(ExprOperator.CloseParen)) {
                        --depth;
                        if (depth < 1) {
                            break;
                        }
                    }
                }
                if (j == input.Length) {
                    err = $"Unclosed parentheses (token {i}): reached end of input";
                    return false;
                }
                if (!ParseArray(input[(i+1)..j], out current, out err)) {
                    return false;
                }
                i = j;
                current.OverridePriority = 5; // parentheses
            }
            else {
                current = new() {
                    Token = input[i],
                    Lhs = null,
                    Rhs = null,
                };
            }

            if (ExpectingOperator(root) && current.ShouldCountAsValue) {
                err = $"unexpected value '{current.Token.Value}': expected operator";
                return false;
            }
            if (ExpectingValue(root) && !current.ShouldCountAsValue) {
                // support unary minus by trying to get an int out of next token
                if (token.IsOperator(ExprOperator.Subtract) && i+1 < input.Length) {
                    if (!ParseArray(input[(i+1)..(i+2)], out ExprNode next, out err)) {
                        return false;
                    }
                    if (next.Token.Type == ExprTokenType.IntLiteral) {
                        current = next;
                        current.Token.Value = $"{-1 * int.Parse(current.Token.Value)}";
                        ++i;
                    }
                }
                else {
                    err = $"unexpected operator '{current.Token.Value}': expected value";
                    return false;
                }
            }

            if (!AddNode(ref root, current, out err)) {
                return false;
            }
        }
        res = root;
        return true;
    }

    internal static bool AddNode(ref ExprNode root, ExprNode cand, out string err)
    {
        err = null;
        if (root is null) {
            root = cand;
            return true;
        }
        ExprNode walker = root;
        if (cand.ShouldCountAsValue) {
            while (walker.Rhs is not null) {
                walker = walker.Rhs;
            }
            walker.Rhs = cand;
            return true;
        }
        else if (cand.IsOperator) {
            ExprNode prev = null;
            while (walker.IsOperator && PriorityOf(cand) > PriorityOf(walker)) {
                prev = walker;
                walker = walker.Rhs;
            }
            cand.Lhs = walker;
            if (System.Object.ReferenceEquals(walker, root)) {
                root = cand;
            }
            if (prev is not null) {
                prev.Rhs = cand;
            }
            return true;
        }
        else {
            err = $"uh oh";
            return false;
        }
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
            // FIXME negative ints
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
                if (!OpDict.TryGetValue(val, out ExprOperator match)) {
                    err = $"Unknown operator '{val}'";
                    return false;
                }
                cons.Add(new ExprToken() {
                    Type = ExprTokenType.Operator,
                    Operator = match,
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
        // get the biggest operator we can (i.e. stop when input becomes invalid)
        while (here < input.Length) {
            char c = input[here];
            if (char.IsWhiteSpace(c) || char.IsLetterOrDigit(c)) {
                break;
            }
            if (!OpDict.TryGetValue(input[index..(here+1)], out var _)) {
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

}

