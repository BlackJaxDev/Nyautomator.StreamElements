using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Nyautomator.StreamElements
{
    public class StreamElementsEvent
    {
        public override string ToString()
            => JsonConvert.SerializeObject(this, Formatting.Indented);

        [JsonPropertyName("channel")]
        [JsonProperty("channel")]
        public string? Channel { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonPropertyName("provider")]
        [JsonProperty("provider")]
        public string? Provider { get; set; }

        [JsonPropertyName("data")]
        [JsonProperty("data")]
        public EventData? Data { get; set; }

        [JsonPropertyName("activityId")]
        [JsonProperty("activityId")]
        public string? ActivityId { get; set; }

        [JsonPropertyName("isMock")]
        [JsonProperty("isMock")]
        public bool IsMock { get; set; }
    }
}