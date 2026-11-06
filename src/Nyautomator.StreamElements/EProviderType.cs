using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Nyautomator.StreamElements
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EProviderType
    {
        twitch,
        youtube,
        facebook
    }
}