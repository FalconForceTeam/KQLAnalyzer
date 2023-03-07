using System.Text.Json.Serialization;

public class LocalData
{
    public LocalData()
    {
        this.Watchlists = new Dictionary<string, WatchlistDetails>();
        this.Tables = new Dictionary<string, TableDetails>();
        this.TabularFunctions = new Dictionary<string, TabularFunctionDetails>();
        this.ScalarFunctions = new Dictionary<string, ScalarFunctionDetails>();
    }

    [JsonPropertyName("watchlists")]
    public Dictionary<string, WatchlistDetails> Watchlists { get; set; }

    [JsonPropertyName("tables")]
    public Dictionary<string, TableDetails> Tables { get; set; }

    [JsonPropertyName("tabular_functions")]
    public Dictionary<string, TabularFunctionDetails> TabularFunctions { get; set; }

    [JsonPropertyName("scalar_functions")]
    public Dictionary<string, ScalarFunctionDetails> ScalarFunctions { get; set; }
}
