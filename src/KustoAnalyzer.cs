using System.Text.Json;
using System.Text.RegularExpressions;
using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Syntax;

namespace KQLAnalyzer
{
    public static class KustoAnalyzer
    {
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

        public static HashSet<string> GetQueryFunctions(KustoCode code)
        {
            var functions = new HashSet<string>();

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (n is FunctionCallExpression fce && fce.ReferencedSymbol is FunctionSymbol fs)
                    {
                        functions.Add(fs.Name);
                    }

                    // Find dynamic expressions
                    if (n is DynamicExpression de)
                    {
                        functions.Add("dynamic");
                    }

                    // Find materialize expressions
                    if (n is MaterializeExpression me)
                    {
                        functions.Add("materialize");
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

        public static HashSet<VariableSymbol> GetQueryVariables(KustoCode code)
        {
            var variables = new HashSet<VariableSymbol>();

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (
                        n.ReferencedSymbol is VariableSymbol v
                    )
                    {
                        variables.Add(v);
                    }
                }
            );

            return variables;
        }

        public static HashSet<OperatorSymbol> GetQueryOperators(KustoCode code)
        {
            var operators = new HashSet<OperatorSymbol>();

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (n.ReferencedSymbol is OperatorSymbol op)
                    {
                        operators.Add(op);
                    }
                }
            );
            return operators;
        }

        public static HashSet<string> GetQueryTabularOperators(KustoCode code)
        {
            var tabularOperators = new HashSet<string>();
            var allOperators = code.Syntax.GetDescendants<QueryOperator>();

            foreach (var op in allOperators)
            {
                // The first token of the operator is the keyword.
                // See https://github.com/microsoft/Kusto-Query-Language/blob/a121c72b7b77e9977fd65aab065d0e0238285cde/src/Kusto.Language/Parser/QueryParser.cs#L4332
                string? operatorKeyword = op.GetFirstToken()?.Text;

                if (operatorKeyword is not null )
                {
                    tabularOperators.Add(operatorKeyword);
                }
            }

            // Other tabular operators (based on https://learn.microsoft.com/en-us/kusto/query/queries?view=microsoft-fabric) that don't fall under the QueryOperator class
            // e.g. https://github.com/microsoft/Kusto-Query-Language/blob/a121c72b7b77e9977fd65aab065d0e0238285cde/src/Kusto.Language.Generators/SyntaxNodeInfos.cs#L2749
            // Notice that Base = "Expression", not "QueryOperator".
            var otherTabularOperators = new HashSet<SyntaxKind>
            {
                SyntaxKind.ExternalDataExpression,
                SyntaxKind.DataTableExpression,
            };

            var allNodes = code.Syntax.GetDescendants<SyntaxNode>();

            foreach (var node in allNodes)
            {
                if (otherTabularOperators.Contains(node.Kind))
                {
                    string cleanName = string.Empty;
                    string kindName = node.Kind.ToString();

                    if (kindName.EndsWith("Expression"))
                    {
                        cleanName = kindName.Replace("Expression", string.Empty);
                    }
                    else
                    {
                        cleanName = kindName; // Defensive, this doesn't seem to happen in practice.
                    }

                    tabularOperators.Add(cleanName);
                }
            }

            return tabularOperators;
        }

        public static HashSet<string> GetQueryStatements(KustoCode code)
        {
            var statements = new HashSet<string>();

            SyntaxElement.WalkNodes(
                code.Syntax,
                n =>
                {
                    if (n is LetStatement)
                    {
                        statements.Add("let");
                    }
                    else if (n is PatternStatement)
                    {
                        statements.Add("pattern");
                    }
                    else if (n is RestrictStatement)
                    {
                        statements.Add("restrict");
                    }
                    else if (n is QueryParametersStatement)
                    {
                        // See https://github.com/microsoft/Kusto-Query-Language/blob/a121c72b7b77e9977fd65aab065d0e0238285cde/src/Kusto.Language.Generators/SyntaxNodeInfos.cs#L2553
                        // Both 'declare' and 'query_parameters' are identified with the same SyntaxNode
                        statements.Add("query_parameters");
                        statements.Add("declare");
                    }
                    else if (n is SetOptionStatement)
                    { // See http://github.com/microsoft/Kusto-Query-Language/blob/a121c72b7b77e9977fd65aab065d0e0238285cde/src/Kusto.Language.Generators/SyntaxNodeInfos.cs#L2517
                        statements.Add("set");
                    }
                }
            );

            return statements;
        }

        // Helper function that will resolve an expression to a string.
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

        public static AnalyzeResults AnalyzeQuery(string query, GlobalState globals, LocalData localData, bool debug, bool strictMode = false, string queryId = "")
        {
            // Keep track of how long it takes to analyze the query.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var myGlobals = globals;

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

            if (!string.IsNullOrEmpty(queryId) && debug)
            {
                Console.WriteLine($"Analyzing query with ID: {queryId}");
            }

            var code = KustoCode.ParseAndAnalyze(query, myGlobals);

            queryResults.ParsingErrors = code.GetDiagnostics().ToList();
            if (!strictMode && queryResults.ParsingErrors.Any())
            {
                for (int i = queryResults.ParsingErrors.Count - 1; i >= 0; i--)
                {
                    var error = queryResults.ParsingErrors[i];
                    if (error.Code == "KS141")
                    {
                        int startofLineIndex = query.LastIndexOf('\n', error.Start) + 1;
                        int rawEndIndex = query.IndexOf('\n', error.End);
                        int endOfLineIndex = rawEndIndex == -1 ? query.Length : rawEndIndex;
                        string queryLine = query[startofLineIndex..endOfLineIndex];
                        bool hasCoalesce = queryLine.Contains("coalesce(");
                        if (hasCoalesce)
                        {
                            queryResults.ParsingErrors.RemoveAt(i);
                        }
                    }
                }
            }

            queryResults.ReferencedTables = GetDatabaseTables(code).Select(t => t.Name).ToList();
            queryResults.ReferencedDatabaseFunctions = GetDatabaseFunctions(code)
                .Select(t => t.Name)
                .ToList();
            queryResults.ReferencedFunctions = GetQueryFunctions(code)
                .Concat(queryResults.ReferencedDatabaseFunctions)
                .Distinct()
                .ToList();
            if (debug)
            {
                Console.WriteLine("Functions found: " + string.Join(", ", queryResults.ReferencedFunctions));
            }

            queryResults.ReferencedColumns = GetDatabaseTableColumns(code)
                .Select(t => t.Name)
                .ToList();
            queryResults.ReferencedVariables = GetQueryVariables(code)
                .Select(t => t.Name)
                .ToList();
            if (debug)
            {
                Console.WriteLine("Variables found: " + string.Join(", ", queryResults.ReferencedVariables));
            }

            queryResults.ReferencedOperators = GetQueryOperators(code)
                .Select(t => t.Name)
                .ToList();
            queryResults.ReferencedTabularOperators = GetQueryTabularOperators(code)
                .ToList();
            queryResults.ReferencedOperators.AddRange(queryResults.ReferencedTabularOperators);
            if (debug)
            {
                Console.WriteLine("Operators/Keywords found: " + string.Join(", ", queryResults.ReferencedOperators));
            }

            queryResults.ReferencedStatements = GetQueryStatements(code)
                .ToList();
            if (code.ResultType != null)
            {
                // the KQL Parse function introduces a column name that is used to store the parsed content.
                // The KQL analyzer library has functionality to ensure that column names are unique or deduplicated if required.
                // However, the KQL analyzer library does not use this functionality with regards to the Parse function so we need to do it ourselves here.
                var columns = code.ResultType.Members.OfType<ColumnSymbol>();
                var columnDictionary = new Dictionary<string, string>();

                foreach (var col in columns)
                {
                    if (!columnDictionary.ContainsKey(col.Name))
                    {
                        columnDictionary.Add(col.Name, col.Type.Name);
                    }
                    else
                    {
                        if (debug)
                        {
                        Console.WriteLine($"WARNING: Found a duplicate column named '{col.Name}'. Skipping...");
                        }

                        continue;
                    }
                }

                queryResults.OutputColumns = columnDictionary;
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
