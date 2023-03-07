using System.Text.Json.Serialization;

public class TabularFunctionDetails
{
    public TabularFunctionDetails()
    {
        this.OutputColumns = new TableDetails();
        this.Arguments = new List<ArgumentDetails>();
    }

    [JsonPropertyName("output_columns")]
    public TableDetails OutputColumns { get; set; }

    [JsonPropertyName("arguments")]
    public List<ArgumentDetails> Arguments { get; set; }
}
