namespace KQLAnalyzer
{
    public class KQLAnalyzerRESTService
    {
        public static IResult Analyze(AnalyzeRequest data, KQLEnvironments kqlEnvironments, bool debug)
        {
            // Check if environment is in KqlEnvironment.Environments
            if (!kqlEnvironments.ContainsKey(data.Environment))
            {
                return Results.NotFound("Environment not found");
            }

            var globals = kqlEnvironments[data.Environment].ToGlobalState();
            var results = KustoAnalyzer.AnalyzeQuery(data.Query, globals, data.LocalData, debug, data.StrictMode, data.QueryId);
            return Results.Ok(results);
        }

        public static void LaunchRestServer(string bindAddress, KQLEnvironments kqlEnvironments, bool debug)
        {
            var app = WebApplication.Create();
            app.MapGet("/api/environments", () => kqlEnvironments.Keys);
            app.MapPost("/api/analyze", (AnalyzeRequest data) => Analyze(data, kqlEnvironments, debug));
            app.Run(bindAddress);
        }
    }
}
