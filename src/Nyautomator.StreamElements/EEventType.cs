using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Nyautomator.StreamElements
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum EEventType
    {
        cheer,
        follow,
        host,
        raid,
        subscriber,
        tip
    }
}