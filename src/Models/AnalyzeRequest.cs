using System.Text.Json.Serialization;

namespace KQLAnalyzer
{
    public class AnalyzeRequest
    {
        public AnalyzeRequest()
        {
            this.QueryId = string.Empty;
            this.StrictMode = false;
            this.Query = string.Empty;
            this.Environment = string.Empty;
            this.LocalData = new LocalData();
        }

        [JsonPropertyName("environment")]
        public string Environment { get; set; }

        [JsonPropertyName("query")]
        public string Query { get; set; }

        [JsonPropertyName("local_data")]
        public LocalData LocalData { get; set; }

        [JsonPropertyName("query_id")]
        public string QueryId { get; set; }

        [JsonPropertyName("strict_mode")]
        public bool StrictMode { get; set; }
    }
}
