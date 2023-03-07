using System.Text.Json.Serialization;

namespace KQLAnalyzer
{
    public class AnalyzeRequest
    {
        public AnalyzeRequest()
        {
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
    }
}
