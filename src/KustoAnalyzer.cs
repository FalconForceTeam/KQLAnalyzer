using System.Text.Json;
using System.Text.RegularExpressions;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KQLAnalyzer
{
    public static class KustoAnalyzer
    {
        private static readonly Dictionary<string, string> AsimFunctionToTable =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["_Im_AuditEvent"] = "ASimAuditEventLogs",
                ["_Im_AuditEvent_Native"] = "ASimAuditEventLogs",
                ["_ASim_AuditEvent"] = "ASimAuditEventLogs",

                ["_Im_Authentication"] = "ASimAuthenticationEventLogs",
                ["_Im_Authentication_Native"] = "ASimAuthenticationEventLogs",
                ["_ASim_Authentication"] = "ASimAuthenticationEventLogs",

                ["_Im_DhcpEvent"] = "ASimDhcpEventLogs",
                ["_Im_DhcpEvent_Native"] = "ASimDhcpEventLogs",
                ["_ASim_DhcpEvent"] = "ASimDhcpEventLogs",

                ["_Im_Dns"] = "ASimDnsActivityLogs",
                ["_Im_Dns_Native"] = "ASimDnsActivityLogs",
                ["_ASim_Dns"] = "ASimDnsActivityLogs",

                ["_Im_FileEvent"] = "ASimFileEventLogs",
                ["_Im_FileEvent_Native"] = "ASimFileEventLogs",
                ["_ASim_FileEvent"] = "ASimFileEventLogs",

                ["_Im_NetworkSession"] = "ASimNetworkSessionLogs",
                ["_Im_NetworkSession_Native"] = "ASimNetworkSessionLogs",
                ["_ASim_NetworkSession"] = "ASimNetworkSessionLogs",

                ["_Im_ProcessEvent"] = "ASimProcessEventLogs",
                ["_Im_ProcessEvent_Native"] = "ASimProcessEventLogs",
                ["_ASim_ProcessEvent"] = "ASimProcessEventLogs",

                ["_Im_RegistryEvent"] = "ASimRegistryEventLogs",
                ["_Im_RegistryEvent_Native"] = "ASimRegistryEventLogs",
                ["_ASim_RegistryEvent"] = "ASimRegistryEventLogs",

                ["_Im_UserManagement"] = "ASimUserManagementActivityLogs",
                ["_Im_UserManagement_Native"] = "ASimUserManagementActivityLogs",
                ["_ASim_UserManagement"] = "ASimUserManagementActivityLogs",

                ["_Im_WebSession"] = "ASimWebSessionLogs",
                ["_Im_WebSession_Native"] = "ASimWebSessionLogs",
                ["_ASim_WebSession"] = "ASimWebSessionLogs",
            };

        private static readonly HashSet<char> ValidDoubleQuoteEscapes =
        [
            '\\', '"', '\'', '0', 'a', 'b', 'f', 'n', 'r', 't', 'v', 'u', 'x'
        ];

        private static string NormalizeKqlDoubleQuotedStringEscapes(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            var sb = new System.Text.StringBuilder(query.Length + 32);
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < query.Length; i++)
            {
                var ch = query[i];

                if (inSingle)
                {
                    sb.Append(ch);
                    if (ch == '\'')
                    {
                        if (i + 1 < query.Length && query[i + 1] == '\'')
                        {
                            sb.Append(query[i + 1]);
                            i++;
                        }
                        else
                        {
                            inSingle = false;
                        }
                    }
                    continue;
                }

                if (inDouble)
                {
                    if (ch == '\\')
                    {
                        // Handle runs of backslashes as a unit. The parser only fails when an
                        // odd number of slashes appears directly before a non-escape character.
                        // Keeping runs even avoids creating invalid escapes such as \W.
                        var start = i;
                        while (i + 1 < query.Length && query[i + 1] == '\\')
                        {
                            i++;
                        }

                        var runLength = i - start + 1;
                        sb.Append('\\', runLength);

                        if (i + 1 < query.Length)
                        {
                            var next = query[i + 1];
                            if (!ValidDoubleQuoteEscapes.Contains(next) && (runLength % 2) != 0)
                            {
                                sb.Append('\\');
                            }
                        }

                        continue;
                    }

                    sb.Append(ch);
                    if (ch == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }

                sb.Append(ch);
                if (ch == '\'')
                {
                    inSingle = true;
                }
                else if (ch == '"')
                {
                    inDouble = true;
                }
            }

            return sb.ToString();
        }

        private static string NormalizeEmptyDoubleQuotedStrings(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            var sb = new System.Text.StringBuilder(query.Length + 16);
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < query.Length; i++)
            {
                var ch = query[i];

                if (inSingle)
                {
                    sb.Append(ch);
                    if (ch == '\'')
                    {
                        if (i + 1 < query.Length && query[i + 1] == '\'')
                        {
                            sb.Append(query[i + 1]);
                            i++;
                        }
                        else
                        {
                            inSingle = false;
                        }
                    }
                    continue;
                }

                if (inDouble)
                {
                    sb.Append(ch);
                    if (ch == '\\' && i + 1 < query.Length)
                    {
                        sb.Append(query[i + 1]);
                        i++;
                        continue;
                    }

                    if (ch == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }

                if (ch == '"' && i + 1 < query.Length && query[i + 1] == '"')
                {
                    // Normalize empty double-quoted literals to single-quoted literals.
                    // Kusto.Language can otherwise parse this shape as a missing argument.
                    sb.Append("''");
                    i++;
                    continue;
                }

                sb.Append(ch);
                if (ch == '\'')
                {
                    inSingle = true;
                }
                else if (ch == '"')
                {
                    inDouble = true;
                }
            }

            return sb.ToString();
        }

        private static string NormalizeSingleBackslashSingleQuotedLiterals(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            // Some clients convert verbatim @"\" to '\', which Kusto.Language parses as
            // an unterminated single-quoted string. Normalize this invalid token.
            return Regex.Replace(query, @"'\\'", @"'\\\\'");
        }

        private static string NormalizeKqlVerbatimDoubleQuotedStrings(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            // Some migrated rules include C#-style verbatim literals (@"...").
            // KQL expects regular quoted strings, so normalize them before parse.
            const string pattern = "@\"(?:\"\"|[^\"])*\"";
            return Regex.Replace(
                query,
                pattern,
                m =>
                {
                    var raw = m.Value.Substring(2, m.Value.Length - 3);
                    var unescapedQuotes = raw.Replace("\"\"", "\"");
                    var escapedBackslashes = unescapedQuotes.Replace("\\", "\\\\");
                    var escapedQuotes = escapedBackslashes.Replace("\"", "\\\"");
                    return $"\"{escapedQuotes}\"";
                }
            );
        }

        private static string NormalizeExtractRegexLiterals(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            const string pattern = "extract\\(\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            return Regex.Replace(
                query,
                pattern,
                m =>
                {
                    var raw = m.Groups[1].Value;
                    var singleQuoted = raw.Replace("'", "''");
                    return $"extract('{singleQuoted}'";
                }
            );
        }

        private static string NormalizeExtractThreeArgumentCalls(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            var sb = new System.Text.StringBuilder(query.Length + 64);
            var inSingle = false;
            var inDouble = false;

            for (var i = 0; i < query.Length; i++)
            {
                var ch = query[i];

                if (inSingle)
                {
                    sb.Append(ch);
                    if (ch == '\'')
                    {
                        if (i + 1 < query.Length && query[i + 1] == '\'')
                        {
                            sb.Append(query[i + 1]);
                            i++;
                        }
                        else
                        {
                            inSingle = false;
                        }
                    }
                    continue;
                }

                if (inDouble)
                {
                    sb.Append(ch);
                    if (ch == '\\' && i + 1 < query.Length)
                    {
                        sb.Append(query[i + 1]);
                        i++;
                        continue;
                    }
                    if (ch == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }

                if (ch == '\'')
                {
                    inSingle = true;
                    sb.Append(ch);
                    continue;
                }

                if (ch == '"')
                {
                    inDouble = true;
                    sb.Append(ch);
                    continue;
                }

                if (
                    i + 7 < query.Length
                    && string.Compare(query, i, "extract(", 0, 8, StringComparison.OrdinalIgnoreCase) == 0
                    && (i == 0 || !(char.IsLetterOrDigit(query[i - 1]) || query[i - 1] == '_'))
                )
                {
                    var start = i;
                    var open = i + 7;
                    var depth = 1;
                    var j = open + 1;
                    var argInSingle = false;
                    var argInDouble = false;
                    var args = new List<string>();
                    var argStart = j;

                    while (j < query.Length)
                    {
                        var c = query[j];
                        if (argInSingle)
                        {
                            if (c == '\'')
                            {
                                if (j + 1 < query.Length && query[j + 1] == '\'')
                                {
                                    j += 2;
                                    continue;
                                }
                                argInSingle = false;
                            }
                            j++;
                            continue;
                        }

                        if (argInDouble)
                        {
                            if (c == '\\' && j + 1 < query.Length)
                            {
                                j += 2;
                                continue;
                            }
                            if (c == '"')
                            {
                                argInDouble = false;
                            }
                            j++;
                            continue;
                        }

                        if (c == '\'')
                        {
                            argInSingle = true;
                            j++;
                            continue;
                        }

                        if (c == '"')
                        {
                            argInDouble = true;
                            j++;
                            continue;
                        }

                        if (c == '(')
                        {
                            depth++;
                            j++;
                            continue;
                        }

                        if (c == ')')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                args.Add(query.Substring(argStart, j - argStart));
                                break;
                            }
                            j++;
                            continue;
                        }

                        if (c == ',' && depth == 1)
                        {
                            args.Add(query.Substring(argStart, j - argStart));
                            argStart = j + 1;
                        }

                        j++;
                    }

                    if (j < query.Length && depth == 0 && args.Count == 3)
                    {
                        sb.Append("extract(");
                        sb.Append(args[0]);
                        sb.Append(',');
                        sb.Append(args[1]);
                        sb.Append(',');
                        sb.Append(args[2]);
                        sb.Append(",typeof(string))");
                        i = j;
                        continue;
                    }

                    sb.Append(ch);
                    continue;
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        private static bool IsIdentifierStart(char c)
        {
            return c == '_' || char.IsLetter(c);
        }

        private static bool IsIdentifierPart(char c)
        {
            return c == '_' || char.IsLetterOrDigit(c);
        }

        private static bool StartsWithIgnoreCase(string text, int index, string value)
        {
            if (index < 0 || index + value.Length > text.Length)
            {
                return false;
            }

            return string.Compare(text, index, value, 0, value.Length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static string NormalizeWorkspaceQualifiedTableReferences(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return query;
            }

            var sb = new System.Text.StringBuilder(query.Length);
            var inSingle = false;
            var inDouble = false;
            var inLineComment = false;
            var inBlockComment = false;

            for (var i = 0; i < query.Length; i++)
            {
                var ch = query[i];

                if (inLineComment)
                {
                    sb.Append(ch);
                    if (ch == '\n' || ch == '\r')
                    {
                        inLineComment = false;
                    }
                    continue;
                }

                if (inBlockComment)
                {
                    sb.Append(ch);
                    if (ch == '*' && i + 1 < query.Length && query[i + 1] == '/')
                    {
                        sb.Append('/');
                        i++;
                        inBlockComment = false;
                    }
                    continue;
                }

                if (inSingle)
                {
                    sb.Append(ch);
                    if (ch == '\'')
                    {
                        if (i + 1 < query.Length && query[i + 1] == '\'')
                        {
                            sb.Append(query[i + 1]);
                            i++;
                        }
                        else
                        {
                            inSingle = false;
                        }
                    }
                    continue;
                }

                if (inDouble)
                {
                    sb.Append(ch);
                    if (ch == '\\' && i + 1 < query.Length)
                    {
                        sb.Append(query[i + 1]);
                        i++;
                        continue;
                    }

                    if (ch == '"')
                    {
                        inDouble = false;
                    }
                    continue;
                }

                if (ch == '-' && i + 1 < query.Length && query[i + 1] == '-')
                {
                    sb.Append(ch);
                    sb.Append(query[i + 1]);
                    i++;
                    inLineComment = true;
                    continue;
                }

                if (ch == '/' && i + 1 < query.Length && query[i + 1] == '*')
                {
                    sb.Append(ch);
                    sb.Append(query[i + 1]);
                    i++;
                    inBlockComment = true;
                    continue;
                }

                if (ch == '\'')
                {
                    sb.Append(ch);
                    inSingle = true;
                    continue;
                }

                if (ch == '"')
                {
                    sb.Append(ch);
                    inDouble = true;
                    continue;
                }

                if (
                    StartsWithIgnoreCase(query, i, "workspace")
                    && (i == 0 || !IsIdentifierPart(query[i - 1]))
                    && (i + 9 >= query.Length || !IsIdentifierPart(query[i + 9]))
                )
                {
                    var j = i + 9;
                    while (j < query.Length && char.IsWhiteSpace(query[j]))
                    {
                        j++;
                    }

                    if (j < query.Length && query[j] == '(')
                    {
                        j++;
                        while (j < query.Length && char.IsWhiteSpace(query[j]))
                        {
                            j++;
                        }

                        if (j < query.Length && (query[j] == '\'' || query[j] == '"'))
                        {
                            var quote = query[j];
                            j++;
                            var literalClosed = false;

                            while (j < query.Length)
                            {
                                if (quote == '\'' && query[j] == '\'')
                                {
                                    if (j + 1 < query.Length && query[j + 1] == '\'')
                                    {
                                        j += 2;
                                        continue;
                                    }

                                    j++;
                                    literalClosed = true;
                                    break;
                                }

                                if (quote == '"' && query[j] == '"')
                                {
                                    j++;
                                    literalClosed = true;
                                    break;
                                }

                                if (quote == '"' && query[j] == '\\' && j + 1 < query.Length)
                                {
                                    j += 2;
                                    continue;
                                }

                                j++;
                            }

                            if (literalClosed)
                            {
                                while (j < query.Length && char.IsWhiteSpace(query[j]))
                                {
                                    j++;
                                }

                                if (j < query.Length && query[j] == ')')
                                {
                                    j++;
                                    while (j < query.Length && char.IsWhiteSpace(query[j]))
                                    {
                                        j++;
                                    }

                                    if (j < query.Length && query[j] == '.')
                                    {
                                        j++;
                                        while (j < query.Length && char.IsWhiteSpace(query[j]))
                                        {
                                            j++;
                                        }

                                        if (j < query.Length && IsIdentifierStart(query[j]))
                                        {
                                            var identStart = j;
                                            j++;
                                            while (j < query.Length && IsIdentifierPart(query[j]))
                                            {
                                                j++;
                                            }

                                            sb.Append(query.Substring(identStart, j - identStart));
                                            i = j - 1;
                                            continue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                sb.Append(ch);
            }

            return sb.ToString();
        }

        // This function was taken from
        // https://github.com/microsoft/Kusto-Query-Language/blob/master/src/Kusto.Language/readme.md
        public static HashSet<TableSymbol> GetDatabaseTables(KustoCode code)
        {
            var tables = new HashSet<TableSymbol>();

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (n.ReferencedSymbol is TableSymbol t && code.Globals.IsDatabaseTable(t))
                    {
                        tables.Add(t);
                    }
                    else if (
                        n is Expression e
                        && e.ResultType is TableSymbol ts
                        && code.Globals.IsDatabaseTable(ts)
                    )
                    {
                        tables.Add(ts);
                    }
                }
            );

            return tables;
        }

        public static HashSet<FunctionSymbol> GetDatabaseFunctions(KustoCode code)
        {
            var functions = new HashSet<FunctionSymbol>();

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (
                        n.ReferencedSymbol is FunctionSymbol t && code.Globals.IsDatabaseFunction(t)
                    )
                    {
                        functions.Add(t);
                    }
                    else if (
                        n is Expression e
                        && e.ResultType is FunctionSymbol ts
                        && code.Globals.IsDatabaseFunction(ts)
                    )
                    {
                        functions.Add(ts);
                    }
                }
            );

            return functions;
        }

        // This function was taken from
        // https://github.com/microsoft/Kusto-Query-Language/blob/master/src/Kusto.Language/readme.md
        public static HashSet<ColumnSymbol> GetDatabaseTableColumns(KustoCode code)
        {
            var columns = new HashSet<ColumnSymbol>();
            GatherColumns(code.Syntax);
            return columns;

            void GatherColumns(SyntaxNode root)
            {
                SyntaxElement.WalkNodes(
                    root,
                    fnBefore: n =>
                    {
                        if (
                            n.ReferencedSymbol is ColumnSymbol c && code.Globals.GetTable(c) != null
                        )
                        {
                            columns.Add(c);
                        }
                        else if (n.GetCalledFunctionBody() is SyntaxNode body)
                        {
                            GatherColumns(body);
                        }
                    },
                    fnDescend: n =>
                        // skip descending into function declarations since their bodies will be examined by the code above
                        !(n is FunctionDeclaration)
                );
            }
        }

        public static Dictionary<string, HashSet<ColumnSymbol>> GetDatabaseTableColumnsByTable(KustoCode code)
        {
            var columnsByTable = new Dictionary<string, HashSet<ColumnSymbol>>();
            GatherColumns(code.Syntax);
            return columnsByTable;

            void GatherColumns(SyntaxNode root)
            {
                SyntaxElement.WalkNodes(
                    root,
                    fnBefore: n =>
                    {
                        if (n.ReferencedSymbol is ColumnSymbol c)
                        {
                            var table = code.Globals.GetTable(c);
                            if (table != null)
                            {
                                if (!columnsByTable.TryGetValue(table.Name, out var cols))
                                {
                                    cols = new HashSet<ColumnSymbol>();
                                    columnsByTable[table.Name] = cols;
                                }
                                cols.Add(c);
                            }
                        }
                        else if (n.GetCalledFunctionBody() is SyntaxNode body)
                        {
                            GatherColumns(body);
                        }
                    },
                    fnDescend: n => !(n is FunctionDeclaration)
                );
            }
        }

        public static HashSet<string> GetReferencedAsimBackingTables(KustoCode code)
        {
            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (n.ReferencedSymbol is FunctionSymbol functionSymbol)
                    {
                        if (AsimFunctionToTable.TryGetValue(functionSymbol.Name, out var tableName))
                        {
                            tables.Add(tableName);
                        }
                    }
                    else if (n is FunctionCallExpression functionCall)
                    {
                        var calledName = functionCall.Name.SimpleName;
                        if (
                            !string.IsNullOrWhiteSpace(calledName)
                            && AsimFunctionToTable.TryGetValue(calledName, out var tableName)
                        )
                        {
                            tables.Add(tableName);
                        }
                    }
                }
            );

            return tables;
        }


        // It supports constants as well as applications of strcat with constant
        // arguments.
        // It won't work for more complex expressions that call other functions since the
        // Kusto.Language analyzer doesn't have an implementation for those functions.
        // The reason for supporting strcat is that there are many queries that for example
        // do something like this:
        // let RuleName='MyRule';
        // _GetWatchlist(strcat("Watchlist_", RuleName))
        // In theory other functions could be supported as well but they would have to
        // be re-written in C#.
        public static string ResolveStringExpression(Expression expr)
        {
            if (expr == null)
            {
                return string.Empty;
            }

            if (expr.ConstantValue != null)
            {
                return expr.ConstantValue.ToString() ?? string.Empty;
            }

            if (expr is FunctionCallExpression fce)
            {
                // We will resolve strcat calls here, since they are commonly
                // used to build up strings and are not resolved by the Kusto analyzer itself.
                if (fce.Name.ToString() == "strcat")
                {
                    return string.Join(
                        string.Empty,
                        fce.ArgumentList.Expressions
                            .Select(e => ResolveStringExpression(e.Element))
                            .ToList()
                    );
                }
            }

            return string.Empty;
        }

        // The GetWatchlist function uses bag_unpack internally to dynamically add columns to the output.
        public static FunctionSymbol GetWatchlist(Dictionary<string, WatchlistDetails> watchlists)
        {
            return new FunctionSymbol(
                "_GetWatchlist",
                context =>
                {
                    var watchlistAlias = ResolveStringExpression(
                        context.GetArgument("watchlistAlias")
                    );
                    var returnedColumns = new List<ColumnSymbol>
                    {
                        new ColumnSymbol("_DTItemId", ScalarTypes.String),
                        new ColumnSymbol("LastUpdatedTimeUTC", ScalarTypes.DateTime),
                        new ColumnSymbol("SearchKey", ScalarTypes.String),
                        new ColumnSymbol("WatchlistItem", ScalarTypes.Dynamic),
                    };
                    if (
                        watchlistAlias != null
                        && watchlists != null
                        && watchlists.ContainsKey(watchlistAlias)
                    )
                    {
                        returnedColumns = returnedColumns
                            .Concat(
                                watchlists[watchlistAlias]
                                    .Select(
                                        c => new ColumnSymbol(c.Key, ScalarTypes.GetSymbol(c.Value))
                                    )
                                    .ToList()
                            )
                            .ToList();
                    }

                    return new TableSymbol(returnedColumns).WithInheritableProperties(
                        context.RowScope
                    );
                },
                Tabularity.Tabular,
                new Parameter("watchlistAlias", ScalarTypes.String)
            );
        }

        public static AnalyzeResults AnalyzeQuery(string query, GlobalState globals, LocalData localData)
        {
            // Keep track of how long it takes to analyze the query.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var myGlobals = globals;

            query = NormalizeKqlVerbatimDoubleQuotedStrings(query);
            query = NormalizeSingleBackslashSingleQuotedLiterals(query);
            query = NormalizeEmptyDoubleQuotedStrings(query);
            query = NormalizeKqlDoubleQuotedStringEscapes(query);
            query = NormalizeWorkspaceQualifiedTableReferences(query);
            query = NormalizeExtractThreeArgumentCalls(query);
            query = NormalizeExtractRegexLiterals(query);

            // The FileProfile function is special in that it takes a string as a parameter,
            // but the parameter is not quoted. It appears that M365 also pre-processes queries
            // that contain this function to magically add quotes around the first parameter.
            if (globals.Database.Functions.Any(f => f.Name == "FileProfile"))
            {
                // Regex to quote the first parameter of FileProfile if it's not already quoted.
                query = Regex.Replace(
                    query,
                    @"(invoke\s+FileProfile\(\s*)([^\',]+)([,)])",
                    "$1'$2'$3"
                );
            }

            if (localData?.Watchlists != null)
            {
                var customWatchlists = new List<FunctionSymbol>()
                {
                    GetWatchlist(localData.Watchlists)
                };

                myGlobals = myGlobals.WithDatabase(
                    myGlobals.Database.WithMembers(
                        myGlobals.Database.Members.Concat(customWatchlists)
                    )
                );
            }

            if (localData?.Tables != null)
            {
                var customTables = GetTables(localData.Tables);
                myGlobals = myGlobals.WithDatabase(
                    myGlobals.Database.WithMembers(myGlobals.Database.Members.Concat(customTables))
                );
            }

            if (localData?.TabularFunctions != null)
            {
                var customFunctions = GetTabularFunctions(localData.TabularFunctions);
                myGlobals = myGlobals.WithDatabase(
                    myGlobals.Database.WithMembers(
                        myGlobals.Database.Members.Concat(customFunctions)
                    )
                );
            }

            if (localData?.ScalarFunctions != null)
            {
                var customFunctions = GetScalarFunctions(localData.ScalarFunctions);
                myGlobals = myGlobals.WithDatabase(
                    myGlobals.Database.WithMembers(
                        myGlobals.Database.Members.Concat(customFunctions)
                    )
                );
            }

            var queryResults = new AnalyzeResults();

            var code = KustoCode.ParseAndAnalyze(query, myGlobals);

            var asimBackingTables = GetReferencedAsimBackingTables(code);

            queryResults.ParsingErrors = code.GetDiagnostics().ToList();
            queryResults.ReferencedTables = GetDatabaseTables(code)
                .Select(t => t.Name)
                .Concat(asimBackingTables)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            queryResults.ReferencedFunctions = GetDatabaseFunctions(code)
                .Select(t => t.Name)
                .ToList();
            queryResults.ReferencedColumns = GetDatabaseTableColumns(code)
                .Select(t => t.Name)
                .ToList();
            queryResults.ReferencedColumnsByTable = GetDatabaseTableColumnsByTable(code)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.Select(c => c.Name).OrderBy(n => n).ToList()
                );

            // ASIM parser functions return tabular results that may flow through aliases/unions.
            // In those cases Kusto can lose direct table lineage for downstream column references.
            // Backfill column usage per ASIM backing table by intersecting referenced column names
            // with the backing table schema.
            var referencedColumnsSet = new HashSet<string>(
                queryResults.ReferencedColumns,
                StringComparer.OrdinalIgnoreCase
            );
            var databaseTables = myGlobals
                .Database
                .Members
                .OfType<TableSymbol>()
                .ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);

            foreach (var asimTable in asimBackingTables)
            {
                if (!queryResults.ReferencedColumnsByTable.TryGetValue(asimTable, out var tableColumns))
                {
                    tableColumns = new List<string>();
                    queryResults.ReferencedColumnsByTable[asimTable] = tableColumns;
                }

                if (!databaseTables.TryGetValue(asimTable, out var tableSymbol))
                {
                    continue;
                }

                var tableColumnSet = new HashSet<string>(
                    tableColumns,
                    StringComparer.OrdinalIgnoreCase
                );

                foreach (var column in tableSymbol.Columns)
                {
                    if (referencedColumnsSet.Contains(column.Name))
                    {
                        tableColumnSet.Add(column.Name);
                    }
                }

                queryResults.ReferencedColumnsByTable[asimTable] = tableColumnSet
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (code.ResultType != null)
            {
                queryResults.OutputColumns = code.ResultType.Members
                    .OfType<ColumnSymbol>()
                    .ToDictionary(c => c.Name, c => c.Type.Name);
            }

            watch.Stop();
            queryResults.ElapsedMs = watch.ElapsedMilliseconds;
            return queryResults;
        }

        private static List<FunctionSymbol> GetScalarFunctions(
            Dictionary<string, ScalarFunctionDetails> functions
        )
        {
            var functionSymbols = new List<FunctionSymbol>();
            foreach (var function in functions)
            {
                var parameters = function.Value.Arguments.Select(
                    p =>
                        new Parameter(
                            p.Name,
                            ScalarTypes.GetSymbol(p.Type),
                            minOccurring: p.Optional ? 0 : 1
                        )
                );
                var functionSymbol = new FunctionSymbol(
                    function.Key,
                    ScalarTypes.GetSymbol(function.Value.OutputType),
                    parameters.ToArray()
                );
                functionSymbols.Add(functionSymbol);
            }

            return functionSymbols;
        }

        private static List<FunctionSymbol> GetTabularFunctions(
            Dictionary<string, TabularFunctionDetails> functions
        )
        {
            var functionSymbols = new List<FunctionSymbol>();
            foreach (var function in functions)
            {
                var parameters = function.Value.Arguments.Select(
                    p =>
                        new Parameter(
                            p.Name,
                            ScalarTypes.GetSymbol(p.Type),
                            minOccurring: p.Optional ? 0 : 1
                        )
                );
                var functionSymbol = new FunctionSymbol(
                    function.Key,
                    context =>
                    {
                        var returnedColumns = function.Value.OutputColumns.Select(
                            c => new ColumnSymbol(c.Key, ScalarTypes.GetSymbol(c.Value))
                        );
                        return new TableSymbol(returnedColumns).WithInheritableProperties(
                            context.RowScope
                        );
                    },
                    Tabularity.Tabular,
                    parameters.ToArray()
                );
                functionSymbols.Add(functionSymbol);
            }

            return functionSymbols;
        }

        private static List<TableSymbol> GetTables(Dictionary<string, TableDetails> tables)
        {
            var tableSymbols = new List<TableSymbol>();
            foreach (var table in tables)
            {
                var columns = table.Value.Select(
                    c => new ColumnSymbol(c.Key, ScalarTypes.GetSymbol(c.Value))
                );
                var tableSymbol = new TableSymbol(table.Key, columns);
                tableSymbols.Add(tableSymbol);
            }

            return tableSymbols;
        }
    }
}
