using CrateDiggin.Api.Models;
using CrateDiggin.Worker;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Qdrant.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// 1. Get Connections
var qdrantEndpoint = builder.Configuration["ConnectionStrings:Qdrant"]
    ?? throw new InvalidOperationException("ConnectionStrings:Qdrant is not configured. Ensure the Worker is referenced by the AppHost with .WithReference(qdrant).");
var ollamaEndpoint = builder.Configuration["ConnectionStrings:Ollama"]
    ?? throw new InvalidOperationException("ConnectionStrings:Ollama is not configured. Ensure the Worker is referenced by the AppHost with .WithReference(ollama).");


// 2. Register Kernel
var kernelBuilder = builder.Services.AddKernel();

// 3. Register Embeddings (Ollama)
builder.Services.AddOllamaTextEmbeddingGeneration(
    modelId: "nomic-embed-text",
    endpoint: new Uri(ollamaEndpoint));

// 4. Register Qdrant (Manual Client + Connector)
builder.Services.AddSingleton<QdrantClient>(_ => new QdrantClient(new Uri(qdrantEndpoint)));
kernelBuilder.AddQdrantVectorStore();

// 5. Register Schema
builder.Services.AddSingleton<IVectorStoreRecordCollection<Guid, Album>>(sp =>
{
    var vectorStore = sp.GetRequiredService<IVectorStore>();
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
            new VectorStoreRecordVectorProperty("Vector", typeof(ReadOnlyMemory<float>)) { Dimensions = 768 }
        }
    };
    return vectorStore.GetCollection<Guid, Album>("albums", recordDefinition);
});

// 6. Register Worker
builder.Services.AddHttpClient();
builder.Services.AddHostedService<InspirationWorker>();

var host = builder.Build();
host.Run();
