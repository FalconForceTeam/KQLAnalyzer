using System.Text.Json;
using KQLAnalyzer;
using Kusto.Language;
using Xunit;

namespace KQLAnalyzerTests
{
    public class InferredFunctionTests
    {
        private static TabularFunctionDetails Function(string parameters, string query)
        {
            return new TabularFunctionDetails { OutputType = "inferred", Parameters = parameters, Query = query };
        }

        private static AnalyzeResults Analyze(string query, params (string Name, TabularFunctionDetails Details)[] functions)
        {
            return KustoAnalyzer.AnalyzeQuery(query, GlobalState.Default,
                new LocalData { TabularFunctions = functions.ToDictionary(f => f.Name, f => f.Details) }, false, strictMode: true);
        }

        [Fact]
        public void InfersExampleFromJson()
        {
            var function = JsonSerializer.Deserialize<TabularFunctionDetails>("""
                {
                    "arguments": [{ "name": "T", "type": "(*)", "optional": false }],
                    "output_type": "inferred",
                    "parameters": "T:(*)",
                    "query": "T | extend foo=\"bar\""
                }
                """)!;
            var result = Analyze("print a=1 | invoke ExtendExample()", ("ExtendExample", function));
            Assert.Empty(result.ParsingErrors);
            Assert.Equal(new Dictionary<string, string> { { "a", "long" }, { "foo", "string" } }, result.OutputColumns);
            Assert.Contains("ExtendExample", result.ReferencedDatabaseFunctions);
        }

        [Fact]
        public void InfersSeparatelyForEachCallSite()
        {
            var result = Analyze("""
                let A = (print value=1) | invoke Identity();
                let B = (print value='text', extra=true) | invoke Identity();
                union A, B
                """, ("Identity", Function("T:(*)", "T")));
            Assert.Empty(result.ParsingErrors);
            Assert.Equal(new Dictionary<string, string>
            {
                { "value_long", "long" }, { "value_string", "string" }, { "extra", "bool" },
            }, result.OutputColumns);
        }

        [Theory]
        [InlineData("(T:(*), label:string='default')", "{ T | project value=label }")]
        [InlineData("T:(*), label:string='default'", "T | project value=label")]
        public void SupportsDefaultsAndWrappedDefinitions(string parameters, string body)
        {
            var result = Analyze("print a=1 | invoke ProjectLabel()", ("ProjectLabel", Function(parameters, body)));
            Assert.Empty(result.ParsingErrors);
            Assert.Equal(new Dictionary<string, string> { { "value", "string" } }, result.OutputColumns);
        }

        [Fact]
        public void SupportsNestedFunctionsLetsJoinsAndAggregation()
        {
            var result = Analyze("""
                datatable(key:string, value:long) ['x', 1] | invoke Enrich()
                """,
                ("Enrich", Function("T:(*)", """
                    let Totals = T | summarize total=sum(value) by key;
                    Totals | join kind=inner (Labels()) on key
                    | project key, total, label
                    """)),
                ("Labels", Function("", "datatable(key:string, label:string) ['x', 'example']")));
            Assert.Empty(result.ParsingErrors);
            Assert.Equal(new Dictionary<string, string>
            {
                { "key", "string" }, { "total", "long" }, { "label", "string" },
            }, result.OutputColumns);
        }

        [Fact]
        public void SupportsDirectCallsAndTypedTableParameters()
        {
            var result = Analyze("SelectValue(datatable(value:long, extra:string) [1, 'x'], 2)",
                ("SelectValue", Function("T:(value:long), factor:long", "T | project scaled=value * factor")));
            Assert.Empty(result.ParsingErrors);
            Assert.Equal(new Dictionary<string, string> { { "scaled", "long" } }, result.OutputColumns);
        }

        [Theory]
        [InlineData("print value=1 | invoke Scale()")]
        [InlineData("print value=1 | invoke Scale(2, 3)")]
        [InlineData("print other=1 | invoke Scale(2)")]
        public void ReportsInvalidArguments(string query)
        {
            var result = Analyze(query, ("Scale", Function("T:(value:long), factor:long", "T | project scaled=value * factor")));
            Assert.NotEmpty(result.ParsingErrors);
        }

        [Fact]
        public void ChecksDownstreamQueryAgainstInferredSchema()
        {
            var result = Analyze("print a=1 | invoke ProjectLabel() | project a",
                ("ProjectLabel", Function("T:(*)", "T | project label='example'")));
            Assert.NotEmpty(result.ParsingErrors);
        }

        [Fact]
        public void ResolvesLocalTablesAndScalarFunctionsInBody()
        {
            var localData = new LocalData
            {
                Tables = new Dictionary<string, TableDetails>
                {
                    { "Lookup", new TableDetails { { "key", "long" }, { "label", "string" } } },
                },
                ScalarFunctions = new Dictionary<string, ScalarFunctionDetails>
                {
                    { "Transform", new ScalarFunctionDetails
                        {
                            OutputType = "string",
                            Arguments = new List<ArgumentDetails> { new ArgumentDetails { Name = "value", Type = "string" } },
                        }
                    },
                },
                TabularFunctions = new Dictionary<string, TabularFunctionDetails>
                {
                    { "Enrich", Function("T:(*)", "T | join kind=inner Lookup on key | project key, label=Transform(label)") },
                },
            };
            var result = KustoAnalyzer.AnalyzeQuery(
                "print key=1 | invoke Enrich() | project result=strcat(label, tostring(key))",
                GlobalState.Default, localData, false, strictMode: true);
            Assert.Empty(result.ParsingErrors);
            Assert.Equal(new Dictionary<string, string> { { "result", "string" } }, result.OutputColumns);
        }

        [Theory]
        [InlineData(null, "print a=1")]
        [InlineData("", null)]
        [InlineData("", " ")]
        public void RejectsIncompleteDefinitions(string? parameters, string? body)
        {
            var function = new TabularFunctionDetails { OutputType = "inferred", Parameters = parameters, Query = body };
            var error = Assert.Throws<ArgumentException>(() => Analyze("Incomplete()", ("Incomplete", function)));
            Assert.Contains("Incomplete", error.Message);
        }
    }
}
