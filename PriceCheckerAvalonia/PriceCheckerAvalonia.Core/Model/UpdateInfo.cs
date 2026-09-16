using System;
using System.Text.Json.Serialization;

namespace PriceCheckerAvalonia.Core.Model
{
    public class UpdateInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("publishedAt")]
        public DateTime? PublishedAt { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("minClientVersion")]
        public string? MinClientVersion { get; set; }

        [JsonPropertyName("signatureUrl")]
        public string? SignatureUrl { get; set; }
    }
}
