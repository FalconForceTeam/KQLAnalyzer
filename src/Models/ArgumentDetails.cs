using System.Text.Json.Serialization;

public class ArgumentDetails
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("optional")]
    public bool Optional { get; set; }
}
