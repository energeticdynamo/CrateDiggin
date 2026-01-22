using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CrateDiggin.Api.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Embeddings;

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

        // 2. Define tags to "dig" for
        var tags = new[] { "jazz", "electronic", "90s", "soul", "ambient", "hip hop" };

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await collection.CreateCollectionIfNotExistsAsync(stoppingToken);

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
        // Fetch top 5 albums for this tag
        var url = $"http://ws.audioscrobbler.com/2.0/?method=tag.gettopalbums&tag={tag}&api_key={_apiKey}&format=json&limit=5";

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

            // Get the "Large" image (index 2 usually)
            var coverUrl = "";
            if (albumData.TryGetProperty("image", out var images) && images.GetArrayLength() > 2)
            {
                coverUrl = images[2].GetProperty("#text").GetString();
            }

            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title)) continue;

            // Generate deterministic ID
            var id = GenerateDeterministicId($"{artist}-{title}");
            var desc = $"{tag} vibes. Album {title} by {artist}.";

            // Vectorize!
            var vector = await embeddingService.GenerateEmbeddingAsync(desc, cancellationToken: ct);

            var album = new Album
            {
                Id = id,
                Artist = artist,
                Title = title,
                Description = desc, // In a real app, we'd fetch the specific album info to get a better description
                CoverUrl = coverUrl ?? "",
                LastFmUrl = lastFmUrl ?? "",
                Vector = vector
            };

            await collection.UpsertAsync(album, cancellationToken: ct);
            logger.LogInformation("Stored: {Title}", title);
        }
    }

    private static Guid GenerateDeterministicId(string input)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return new Guid(hash);
    }
}
