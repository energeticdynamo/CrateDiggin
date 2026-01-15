using CrateDiggin.Api.Models;
using CrateDiggin.Api.Services;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.Qdrant; // Required for AddQdrantVectorStore
using Qdrant.Client; // Required for QdrantClient
using Scalar.AspNetCore;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();

var qdrantEndpoint = builder.Configuration["ConnectionStrings:Qdrant"];
var ollamaEndpoint = builder.Configuration["ConnectionStrings:Ollama"];
var geminiApiKey = builder.Configuration["Gemini:ApiKey"];

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

builder.Services.AddTransient<SeedingService>();

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

app.MapPost("/fill-crates", async (CrateDiggin.Api.Services.SeedingService seeder) =>
{
    var result = await seeder.SeedCratesAsync();
    return Results.Ok(result);
});

app.Run();