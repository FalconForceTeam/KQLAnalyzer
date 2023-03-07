using Xunit;
using KQLAnalyzer;
using System.Text.Json;

namespace KQLAnalyzerTests
{
    public class KQLAnalyzerTests
    {
        public static KQLEnvironments kqlEnvironments = JsonSerializer.Deserialize<KQLEnvironments>(
            File.ReadAllText("environments.json")
        )!;

        public static AnalyzeResults AnalyzeFromJson(string inputFile)
        {
            var analyzeRequest = JsonSerializer.Deserialize<AnalyzeRequest>(
                File.ReadAllText(inputFile)
            );

            var environmentName = analyzeRequest!.Environment;
            var globals = kqlEnvironments[environmentName].ToGlobalState();

            var results = KustoAnalyzer.AnalyzeQuery(
                analyzeRequest.Query,
                globals,
                analyzeRequest.LocalData
            );
            return results;
        }

        private static void WriteResults(AnalyzeResults results)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    results,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }

        [Fact]
        public void SimpleQuery()
        {
            var results = AnalyzeFromJson("test_data/simple_query.json");
            Assert.Empty(results.ParsingErrors);
            Assert.Equal(results.OutputColumns, new Dictionary<string, string> { { "a", "bool" } });
        }

        [Fact]
        public void SimpleQueryDefaultTables()
        {
            var results = AnalyzeFromJson("test_data/simple_query_using_default_tables.json");
            Assert.Empty(results.ParsingErrors);
            Assert.Equal(
                results.OutputColumns,
                new Dictionary<string, string> { { "a", "string" } }
            );
        }


        [Fact]
        public void SimpleQueryCustomTables()
        {
            var results = AnalyzeFromJson("test_data/custom_table.json");
            Assert.Empty(results.ParsingErrors);
            Assert.Equal(
                results.OutputColumns,
                new Dictionary<string, string> { { "Value", "string" } }
            );
        }

        [Fact]
        public void SentinelNoFileProfile()
        {
            var results = AnalyzeFromJson("test_data/sentinel_no_fileprofile.json");
            Assert.NotEmpty(
                results.ParsingErrors.Where(
                    e => e.Code == "KS211" && e.Message.Contains("FileProfile")
                )
            );
        }

        [Fact]
        public void FileProfile()
        {
            var results = AnalyzeFromJson("test_data/fileprofile.json");
            Assert.Empty(results.ParsingErrors);
            Assert.NotEmpty(results.OutputColumns.Where(e => e.Key == "Issuer")); // Add FileProfile columns
            // SHA1 exists in both input and in FileProfile so there should also be a SHA11
            Assert.NotEmpty(results.OutputColumns.Where(e => e.Key == "SHA1"));
            Assert.NotEmpty(results.OutputColumns.Where(e => e.Key == "SHA11"));
            Assert.NotEmpty(results.OutputColumns.Where(e => e.Key == "Foo")); // Original input column is preserved
        }

        [Fact]
        public void TabularFunction()
        {
            var results = AnalyzeFromJson("test_data/tabular_function.json");
            Assert.Empty(results.ParsingErrors);
            Assert.Equal(
                results.OutputColumns,
                new Dictionary<string, string> { { "output_foo", "string" } }
            );
            Assert.Equal(results.ReferencedFunctions, new List<string> { "MyFunction" });
        }

        [Fact]
        public void TabularFunctionRequiredArgs()
        {
            var results = AnalyzeFromJson("test_data/tabular_function_required_args.json");
            Assert.NotEmpty(results.ParsingErrors.Where(e => e.Code == "KS119")); // Expect KS119 error The function 'MyFunction' expects 1 argument.
        }

        [Fact]
        public void ScalarFunction()
        {
            var results = AnalyzeFromJson("test_data/scalar_function.json");
            Assert.Empty(results.ParsingErrors);
            Assert.Equal(results.OutputColumns, new Dictionary<string, string> { { "a", "bool" } });
            Assert.Equal(results.ReferencedFunctions, new List<string> { "MyScalar" });
        }

        [Fact]
        public void Watchlist()
        {
            var results = AnalyzeFromJson("test_data/watchlist.json");
            Assert.Empty(results.ParsingErrors);
            Assert.Equal(
                results.OutputColumns,
                new Dictionary<string, string>
                {
                    { "_DTItemId", "string" },
                    { "LastUpdatedTimeUTC", "datetime" },
                    { "SearchKey", "string" },
                    { "WatchlistItem", "dynamic" },
                    { "foo", "string" },
                }
            );
            Assert.Equal(results.ReferencedFunctions, new List<string> { "_GetWatchlist" });
        }
    }
}
