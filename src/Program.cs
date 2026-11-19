using System.CommandLine;
using System.Text.Json;
using KQLAnalyzer;

public class Program
{
    /// <summary>
    /// KustoQueryAnalyzer can analyze a Kusto query and returns metadata about the query.
    /// </summary>
    public static int Main(string[] args)
    {
        var inputFile = new Option<FileInfo?>("--input-file") { Description = "Analyze query from JSON file." };
        var environmentsFile = new Option<FileInfo?>("--environments-file") { Description = "Environment configuration file (default: ../environments.json)." };
        var rest = new Option<bool>("--rest") { Description = "Start a REST server to listen for requests." };
        var bindAddress = new Option<string>("--bind-address")
        {
            Description = "HTTP bind address in the format http://host:port.",
            DefaultValueFactory = _ => "http://localhost:8000",
        };
        var debug = new Option<bool>("--debug") { Description = "Enable debug messages." };
        var command = new RootCommand("Analyze a Kusto query and return its metadata.")
        {
            inputFile, environmentsFile, rest, bindAddress, debug,
        };
        command.SetAction(result => Run(
            result.GetValue(inputFile),
            result.GetValue(environmentsFile),
            result.GetValue(rest),
            result.GetValue(bindAddress)!,
            result.GetValue(debug)));
        return command.Parse(args).Invoke();
    }

    private static void Run(FileInfo? inputFile, FileInfo? environmentsFile, bool rest, string bindAddress, bool debug)
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

        // Add merged m365_with_sentinel environment if both m365 and sentinel exist
        EnvironmentUtils.AddM365WithSentinelIfPresent(kqlEnvironments);

        if (rest)
        {
            KQLAnalyzerRESTService.LaunchRestServer(bindAddress, kqlEnvironments, debug);
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
                analyzeRequest.LocalData,
                debug
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
