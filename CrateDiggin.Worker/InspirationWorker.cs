using CrateDiggin.Api.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Embeddings;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CrateDiggin.Worker;

public class InspirationWorker(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    IVectorStoreRecordCollection<Guid, Album> collection,
    ITextEmbeddingGenerationService embeddingService,
    ILogger<InspirationWorker> logger) : BackgroundService
{
    private readonly string _apiKey = config["LastFm:ApiKey"] ?? throw new ArgumentNullException("LastFm ApiKey missing");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Wait for services to wake up
        logger.LogInformation("Worker starting... waiting 10s for Qdrant/Ollama.");
        await Task.Delay(10000, stoppingToken);


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await collection.CreateCollectionIfNotExistsAsync(stoppingToken);

                var tags = config.GetSection("DiggingTags").Get<string[]>();

                // Fallback if config is empty
                if (tags == null || tags.Length == 0)
                {
                    logger.LogWarning("No tags found in appsettings.json! Using defaults.");
                    tags = ["jazz", "electronic", "90s", "soul", "ambient", "hip hop", "rnb", "rock"];
                }

                foreach (var tag in tags)
                {
                    logger.LogInformation("Digging for tag: {Tag}", tag);
                    await FetchAndStoreAlbums(tag, stoppingToken);

                    // Be polite to the API
                    await Task.Delay(2000, stoppingToken);
                }

                logger.LogInformation("Crates refreshed. Sleeping for 1 hour.");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker crashed! Retrying in 1 minute.");
                await Task.Delay(60000, stoppingToken);
            }
        }
    }

    private async Task FetchAndStoreAlbums(string tag, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient();

        var randomPage = Random.Shared.Next(1, 20);
        // Fetch top 5 albums for this tag
        var url = $"http://ws.audioscrobbler.com/2.0/?method=tag.gettopalbums&tag={tag}&api_key={_apiKey}&format=json&limit=5&page={randomPage}";

        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return;

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

        // Safety check for empty results
        if (!doc.RootElement.TryGetProperty("albums", out var albumsRoot) ||
            !albumsRoot.TryGetProperty("album", out var albumsArray))
            return;

        foreach (var albumData in albumsArray.EnumerateArray())
        {
            var artist = albumData.GetProperty("artist").GetProperty("name").GetString();
            var title = albumData.GetProperty("name").GetString();
            var lastFmUrl = albumData.GetProperty("url").GetString();

            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title)) continue;

            var details = await GetAlbumDetails(client, artist, title, ct);

            var description = string.IsNullOrEmpty(details)
            ? $"{artist} - {title}. Music style and genre: {tag}. A {tag} album with {tag} vibes and influences."
            : details;

            // Get the "Large" image (index 2 usually)
            var coverUrl = "";
            if (albumData.TryGetProperty("image", out var images) && images.GetArrayLength() > 2)
            {
                coverUrl = images[2].GetProperty("#text").GetString();
            }

            // Generate deterministic ID
            var id = GenerateDeterministicId($"{artist}-{title}");

            // Vectorize!
            var vector = await embeddingService.GenerateEmbeddingAsync(description, cancellationToken: ct);

            var album = new Album
            {
                Id = id,
                Artist = artist,
                Title = title,
                Description = description, // In a real app, we'd fetch the specific album info to get a better description
                CoverUrl = coverUrl ?? "",
                LastFmUrl = lastFmUrl ?? "",
                Vector = vector
            };

            await collection.UpsertAsync(album, cancellationToken: ct);
            logger.LogInformation("Stored: {Title} (Rich Data)", title);
        }
    }

    private async Task<string> GetAlbumDetails(HttpClient client, string artist, string album, CancellationToken ct)
    {
        try
        {
            var url = $"http://ws.audioscrobbler.com/2.0/?method=album.getinfo&api_key={_apiKey}&artist={Uri.EscapeDataString(artist)}&album={Uri.EscapeDataString(album)}&format=json";
            var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return "";

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty("album", out var albumElement)) return "";

            // 1. Get Wiki Summary
            var summary = "";
            if (albumElement.TryGetProperty("wiki", out var wiki) && wiki.TryGetProperty("summary", out var summaryProp))
            {
                summary = summaryProp.GetString();
                // Strip HTML links from Last.fm summary
                if (!string.IsNullOrEmpty(summary))
                {
                    var index = summary.IndexOf("<a href");
                    if (index > 0) summary = summary.Substring(0, index);
                }
            }

            // 2. Get Specific Tags (e.g., "alternative rock", "lo-fi", "r&b")
            var tagsList = new List<string>();
            if (albumElement.TryGetProperty("tags", out var tagsRoot) && tagsRoot.TryGetProperty("tag", out var tagsArray))
            {
                foreach (var t in tagsArray.EnumerateArray())
                {
                    if (t.TryGetProperty("name", out var tagName))
                        tagsList.Add(tagName.GetString() ?? "");
                }
            }

            // 3. Get release year if available
            var year = "";
            if (albumElement.TryGetProperty("wiki", out var wikiElement) &&
                wikiElement.TryGetProperty("published", out var published))
            {
                var publishedStr = published.GetString();
                if (!string.IsNullOrEmpty(publishedStr) && publishedStr.Length >= 4)
                {
                    // Extract year from date string
                    var yearMatch = System.Text.RegularExpressions.Regex.Match(publishedStr, @"\b(19|20)\d{2}\b");
                    if (yearMatch.Success) year = yearMatch.Value;
                }
            }

            // Build rich description with era context
            var genres = string.Join(", ", tagsList.Take(7));
            var eraContext = !string.IsNullOrEmpty(year) ? $"Released in {year}. " : "";

            return $"{artist} - {album}. {eraContext}Genres and styles: {genres}. {summary}".Trim();
        }
        catch
        {
            return "";
        }
    }

    private static Guid GenerateDeterministicId(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
