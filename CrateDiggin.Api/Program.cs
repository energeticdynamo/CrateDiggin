using CrateDiggin.Api.Models;
using CrateDiggin.Api.Plugins;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using Scalar.AspNetCore;


var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

var qdrantEndpoint = builder.Configuration["ConnectionStrings:Qdrant"]
    ?? throw new InvalidOperationException("ConnectionStrings:Qdrant is not configured.");

var ollamaEndpoint = builder.Configuration["ConnectionStrings:Ollama"]
    ?? throw new InvalidOperationException("ConnectionStrings:Ollama is not configured.");

var geminiApiKey = builder.Configuration["Gemini:ApiKey"]
    ?? throw new InvalidOperationException("ConnectionStrings: Gemini is not configured");

// --- 1. Register Kernel & Capture Builder ---
var kernelBuilder = builder.Services.AddKernel();

// --- 2. Configure Chat (Gemini) ---
//builder.Services.AddGoogleAIGeminiChatCompletion(
//    modelId: "gemini-2.0-flash-lite",
//    apiKey: geminiApiKey);

builder.Services.AddOllamaChatCompletion(
    modelId: "mistral",
    endpoint: new Uri(ollamaEndpoint));

// --- 3. Configure Embeddings (Ollama) ---
builder.Services.AddOllamaTextEmbeddingGeneration(
    modelId: "nomic-embed-text",
    endpoint: new Uri(ollamaEndpoint));

// --- 4. Configure Vector Store (Qdrant) ---
// FIX: Register the QdrantClient manually first. 
// This is the most robust way to handle the connection in the Alpha version.
builder.Services.AddSingleton<QdrantClient>(_ =>
{
    var uri = new Uri(qdrantEndpoint);
    return new QdrantClient(uri.Host, uri.Port);
});

// Now tell Semantic Kernel to use the registered client
kernelBuilder.AddQdrantVectorStore();

// --- 5. Register the Album Collection with Manual Schema ---
builder.Services.AddSingleton<IVectorStoreRecordCollection<Guid, Album>>(sp =>
{
    var vectorStore = sp.GetRequiredService<IVectorStore>();

    // Manual Schema Definition
    var recordDefinition = new VectorStoreRecordDefinition
    {
        Properties = new List<VectorStoreRecordProperty>
        {
            new VectorStoreRecordKeyProperty("Id", typeof(Guid)),
            new VectorStoreRecordDataProperty("Artist", typeof(string)) { IsFilterable = true },
            new VectorStoreRecordDataProperty("Title", typeof(string)) { IsFilterable = true },
            new VectorStoreRecordDataProperty("Description", typeof(string)),
            new VectorStoreRecordDataProperty("CoverUrl", typeof(string)),
            new VectorStoreRecordDataProperty("LastFmUrl", typeof(string)),
            // Dimensions are defined here, so we don't need them in the Options above
            new VectorStoreRecordVectorProperty("Vector", typeof(ReadOnlyMemory<float>)) { Dimensions = 768 }
        }
    };

    return vectorStore.GetCollection<Guid, Album>("albums", recordDefinition);
});

builder.Services.AddTransient<CrateDiggingPlugin>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    app.MapGet("/", () => Results.Redirect("/scalar/v1"));
}

// Test Endpoint: Verify Brain
app.MapGet("/verify-brain", async (Kernel kernel, CancellationToken cancellationToken) =>
{
    const int maxRetries = 3;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            var result = await kernel.InvokePromptAsync("What is the best hip-hop album of 1994? Answer in 5 words.",
                cancellationToken: cancellationToken);
            return Results.Ok(result.ToString());
        }
        catch (HttpOperationException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            if (i == maxRetries - 1) throw;
            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i + 1)), cancellationToken);
        }
    }
    return Results.Problem("Rate limit exceeded after retries");
});

// Test Endpoint: Verify Memory
app.MapPost("/seed-album", async (
    IVectorStoreRecordCollection<Guid, Album> collection,
    ITextEmbeddingGenerationService embeddingService) =>
{
    // Create collection if it doesn't exist
    await collection.CreateCollectionIfNotExistsAsync();

    var album = new Album
    {
        Id = Guid.NewGuid(),
        Artist = "Daft Punk",
        Title = "Discovery",
        Description = "French house, electronic, disco, synthpop, retro futuristic vibes",
        CoverUrl = "https://example.com/daftpunk.jpg"
    };

    Console.WriteLine($"Generating embedding for: {album.Title}...");
    album.Vector = await embeddingService.GenerateEmbeddingAsync(album.Description);

    await collection.UpsertAsync(album);

    return Results.Ok($"Saved '{album.Title}' with vector size {album.Vector.Length} to Qdrant!");
});

// The "Dig" Endpoint: Search for albums matching a vibe
app.MapGet("/dig", async (
    string query,
    CrateDiggin.Api.Plugins.CrateDiggingPlugin diggingPlugin) =>
{
    // Usage: /dig?query=dark techno
    var result = await diggingPlugin.DigCrateAsync(query);

    return Results.Content(result, "application/json");
});

// The Simple Vector Search Endpoint (For the UI Grid)
app.MapGet("/api/search", async (
    string query,
    Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService embeddingService,
    Microsoft.Extensions.VectorData.IVectorStoreRecordCollection<Guid, CrateDiggin.Api.Models.Album> collection) =>
{
    // 1. Enhance the query for better embedding results
    var enhancedQuery = $"Music search: {query}. Looking for albums with this style, genre, and mood.";

    // 2. Generate Vector
    var queryVector = await embeddingService.GenerateEmbeddingAsync(enhancedQuery);

    // 3. Search DB
    var searchResult = await collection.VectorizedSearchAsync(queryVector, new() { Top = 12 });

    int numOfAlbums = 12;

    // 4. Return full album objects (so UI gets CoverUrls)
    var albums = new List<Album>();
    await foreach (var record in searchResult.Results)
    {
        var album = record.Record;
        album.Score = record.Score;
        albums.Add(album);
        if (albums.Count >= numOfAlbums) break;
    }

    return Results.Ok(albums);
});

// Debug endpoint to see what's being matched and why
app.MapGet("/api/search/debug", async (
    string query,
    Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService embeddingService,
    Microsoft.Extensions.VectorData.IVectorStoreRecordCollection<Guid, CrateDiggin.Api.Models.Album> collection) =>
{
    // Use the SAME enhanced query as the main search endpoint
    var enhancedQuery = $"Music search: {query}. Looking for albums with this style, genre, and mood.";

    var queryVector = await embeddingService.GenerateEmbeddingAsync(enhancedQuery);
    var searchResult = await collection.VectorizedSearchAsync(queryVector, new() { Top = 10 });

    var results = new List<object>();
    await foreach (var record in searchResult.Results)
    {
        results.Add(new
        {
            record.Record.Artist,
            record.Record.Title,
            record.Record.Description,
            Score = record.Score,
            ScoreRating = record.Score switch
            {
                >= 0.80f => "Excellent",
                >= 0.70f => "Good",
                >= 0.60f => "Fair",
                _ => "Poor"
            }
        });
    }

    // Calculate average score for quality assessment
    var scores = results.Cast<dynamic>().Select(r => (float)r.Score).ToList();
    var avgScore = scores.Count > 0 ? scores.Average() : 0;

    return Results.Ok(new
    {
        OriginalQuery = query,
        EnhancedQuery = enhancedQuery,
        AverageScore = Math.Round(avgScore, 3),
        OverallQuality = avgScore switch
        {
            >= 0.75f => "Great matches",
            >= 0.65f => "Decent matches",
            >= 0.55f => "Weak matches - consider adding more relevant tags",
            _ => "Poor matches - genre likely missing from database"
        },
        ResultCount = results.Count,
        Results = results
    });
});

app.Run();