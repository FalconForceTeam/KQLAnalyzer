using System.Text.Json.Serialization;
using Kusto.Language;

public class AnalyzeResults
{
    public AnalyzeResults()
    {
        this.OutputColumns = new Dictionary<string, string>();
        this.ParsingErrors = new List<Diagnostic>();
        this.ElapsedMs = 0;
        this.ReferencedTables = new List<string>();
        this.ReferencedDatabaseFunctions = new List<string>();
        this.ReferencedFunctions = new List<string>();
        this.ReferencedColumns = new List<string>();
        this.ReferencedVariables = new List<string>();
        this.ReferencedOperators = new List<string>();
        this.ReferencedTabularOperators = new List<string>();
        this.ReferencedStatements = new List<string>();
    }

    [JsonPropertyName("output_columns")]
    public Dictionary<string, string> OutputColumns { get; set; }

    [JsonPropertyName("parsing_errors")]
    public List<Diagnostic> ParsingErrors { get; set; }

    [JsonPropertyName("referenced_tables")]
    public List<string> ReferencedTables { get; set; }

    [JsonPropertyName("referenced_database_functions")]
    public List<string> ReferencedDatabaseFunctions { get; set; }

    [JsonPropertyName("referenced_functions")]
    public List<string> ReferencedFunctions { get; set; }

    [JsonPropertyName("referenced_columns")]
    public List<string> ReferencedColumns { get; set; }

    [JsonPropertyName("referenced_variables")]
    public List<string> ReferencedVariables { get; set; }

    [JsonPropertyName("referenced_tabular_operators")]
    public List<string> ReferencedTabularOperators { get; set; }

    [JsonPropertyName("referenced_operators")]
    public List<string> ReferencedOperators { get; set; }

    [JsonPropertyName("referenced_statements")]
    public List<string> ReferencedStatements { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; set; }
}
