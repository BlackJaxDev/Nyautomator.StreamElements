using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Nyautomator.StreamElements
{
    public class EventData
    {
        [JsonPropertyName("tipId")]
        [JsonProperty("tipId")]
        public string? TipId { get; set; }

        [JsonPropertyName("username")]
        [JsonProperty("username")]
        public string? Username { get; set; }

        [JsonPropertyName("providerId")]
        [JsonProperty("providerId")]
        public string? ProviderId { get; set; }

        [JsonPropertyName("displayName")]
        [JsonProperty("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonPropertyName("amount")]
        [JsonProperty("amount")]
        public int Amount { get; set; }

        [JsonPropertyName("streak")]
        [JsonProperty("streak")]
        public int? Streak { get; set; }

        [JsonPropertyName("tier")]
        [JsonProperty("tier")]
        public string? Tier { get; set; }

        [JsonPropertyName("currency")]
        [JsonProperty("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("message")]
        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonPropertyName("quantity")]
        [JsonProperty("quantity")]
        public int? Quantity { get; set; }

        [JsonPropertyName("items")]
        [JsonProperty("items")]
        public List<object>? Items { get; set; }

        /// <summary>
        /// Url to an image for the user
        /// </summary>
        [JsonPropertyName("avatar")]
        [JsonProperty("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("createdAt")]
        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        [JsonProperty("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        [JsonPropertyName("sender")]
        [JsonProperty("sender")]
        public string? Sender { get; set; }

        [JsonPropertyName("gifted")]
        [JsonProperty("gifted")]
        public bool Gifted { get; set; }
    }
}