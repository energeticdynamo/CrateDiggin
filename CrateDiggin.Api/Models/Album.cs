namespace CrateDiggin.Api.Models
{
    public class Album
    {
        public Guid Id { get; set; }

        public string Artist { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string CoverUrl { get; set; } = string.Empty;

        public string LastFmUrl { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public double? Score { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public ReadOnlyMemory<float> Vector { get; set; }
    }
}
