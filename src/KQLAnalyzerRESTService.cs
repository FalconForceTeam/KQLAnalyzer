namespace KQLAnalyzer
{
    public class KQLAnalyzerRESTService
    {
        public static IResult Analyze(AnalyzeRequest data, KQLEnvironments kqlEnvironments)
        {
            // Check if environment is in KqlEnvironment.Environments
            if (!kqlEnvironments.ContainsKey(data.Environment))
            {
                return Results.NotFound("Environment not found");
            }

            var globals = kqlEnvironments[data.Environment].ToGlobalState();
            var results = KustoAnalyzer.AnalyzeQuery(data.Query, globals, data.LocalData);
            return Results.Ok(results);
        }

        public static void LaunchRestServer(string bindAddress, KQLEnvironments kqlEnvironments)
        {
            var app = WebApplication.Create();
            app.MapGet("/api/environments", () => kqlEnvironments.Keys);
            app.MapPost("/api/analyze", (AnalyzeRequest data) => Analyze(data, kqlEnvironments));
            app.Run(bindAddress);
        }
    }
}
