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

        public static AnalyzeResults AnalyzeQuery(string query, GlobalState globals, LocalData localData)
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

            var code = KustoCode.ParseAndAnalyze(query, myGlobals);

            queryResults.ParsingErrors = code.GetDiagnostics().ToList();
            queryResults.ReferencedTables = GetDatabaseTables(code).Select(t => t.Name).ToList();
            queryResults.ReferencedFunctions = GetDatabaseFunctions(code)
                .Select(t => t.Name)
                .ToList();
            queryResults.ReferencedColumns = GetDatabaseTableColumns(code)
                .Select(t => t.Name)
                .ToList();
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
