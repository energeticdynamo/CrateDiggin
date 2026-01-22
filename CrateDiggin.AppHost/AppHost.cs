var builder = DistributedApplication.CreateBuilder(args);

// 1. Setup Qdrant
var qdrant = builder.AddContainer("qdrant", "qdrant/qdrant", "latest")
    .WithHttpEndpoint(port: 6333, targetPort: 6333, name: "qdrant-http")
    .WithHttpEndpoint(port: 6334, targetPort: 6334, name: "qdrant-grpc") // Add gRPC endpoint
    .WithBindMount("./qdrant_data", "/qdrant/storage");

// 2. Setup Ollama
var ollama = builder.AddContainer("ollama", "ollama/ollama", "latest")
    .WithHttpEndpoint(port: 11434, targetPort: 11434, name: "ollama-http")
    .WithBindMount("./ollama_data", "/root/.ollama");

// 3. Setup the API Project
builder.AddProject<Projects.CrateDiggin_Api>("api")
    // Fix: Use the gRPC endpoint for QdrantClient
    .WithEnvironment("ConnectionStrings__Qdrant", qdrant.GetEndpoint("qdrant-grpc"))
    .WithEnvironment("ConnectionStrings__Ollama", ollama.GetEndpoint("ollama-http"))
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.CrateDiggin_Worker>("cratediggin-worker")
    .WithEnvironment("ConnectionStrings__Qdrant", qdrant.GetEndpoint("qdrant-grpc"))
    .WithEnvironment("ConnectionStrings__Ollama", ollama.GetEndpoint("ollama-http"));

builder.Build().Run();