using System.Text.Json.Serialization;

public class ScalarFunctionDetails
{
    public ScalarFunctionDetails()
    {
        this.OutputType = string.Empty;
        this.Arguments = new List<ArgumentDetails>();
    }

    [JsonPropertyName("output_type")]
    public string? OutputType { get; set; }

    [JsonPropertyName("arguments")]
    public List<ArgumentDetails> Arguments { get; set; }
}
