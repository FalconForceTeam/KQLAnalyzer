using System.Text.Json;
using KQLAnalyzer;

public class Program
{
    /// <summary>
    /// KustoQueryAnalyzer can analyze a Kusto query and returns metadata about the query.
    /// </summary>
    /// <param name="inputFile">Analyze query from JSON file.</param>
    /// <param name="environmentsFile">Environment configuration file to use. Defaults to ../environments.json.</param>
    /// <param name="rest">Start a REST server to listen for requests.</param>
    /// <param name="bindAddress">HTTP bind address to use in format http://host:port.</param>
#pragma warning disable 8625
    public static void Main(
        FileInfo inputFile = null,
        FileInfo environmentsFile = null,
        bool rest = false,
        string bindAddress = "http://localhost:8000"
    )
#pragma warning restore 8625
    {
        environmentsFile = environmentsFile ?? new FileInfo(Path.Join("..", "environments.json"));
        var kqlEnvironments = new KQLEnvironments();
        try
        {
            kqlEnvironments = JsonSerializer.Deserialize<KQLEnvironments>(
                File.ReadAllText(environmentsFile.FullName)
            )!;
        }
        catch (Exception e)
        {
            Console.WriteLine($"Could not parse environments file {environmentsFile.FullName}: {e.Message}");
            Environment.Exit(1);
        }

        if (rest)
        {
            KQLAnalyzerRESTService.LaunchRestServer(bindAddress, kqlEnvironments);
            return;
        }

        if (inputFile != null)
        {
            var analyzeRequest = new AnalyzeRequest();
            try
            {
                analyzeRequest = JsonSerializer.Deserialize<AnalyzeRequest>(
                    File.ReadAllText(inputFile.FullName)
                )!;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Could not parse input file {inputFile.FullName}: {e.Message}");
                Environment.Exit(1);
            }

            var environmentName = analyzeRequest.Environment;
            if (!kqlEnvironments.TryGetValue(environmentName, out var environment))
            {
                Console.WriteLine($"Could not find environment {environmentName}.");
                Environment.Exit(1);
            }

            var results = KustoAnalyzer.AnalyzeQuery(
                analyzeRequest.Query,
                environment.ToGlobalState(),
                analyzeRequest.LocalData
            );
            Console.WriteLine(
                JsonSerializer.Serialize(
                    results,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
            return;
        }

        Console.WriteLine("Please provide either --input-file or --rest.");
        Environment.Exit(1);
    }
}
