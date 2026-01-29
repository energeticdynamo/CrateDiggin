var builder = DistributedApplication.CreateBuilder(args);

// 1. Setup Qdrant
var qdrant = builder.AddContainer("qdrant", "qdrant/qdrant", "latest")
    .WithHttpEndpoint(port: 6333, targetPort: 6333, name: "qdrant-http")
    .WithHttpEndpoint(port: 6334, targetPort: 6334, name: "qdrant-grpc")
    .WithBindMount("./qdrant_data", "/qdrant/storage");

// 2. Setup Ollama
var ollama = builder.AddContainer("ollama", "ollama/ollama", "latest")
    .WithHttpEndpoint(port: 11434, targetPort: 11434, name: "ollama-http")
    .WithBindMount("./ollama_data", "/root/.ollama");

// 3. Setup the API Project
var api = builder.AddProject<Projects.CrateDiggin_Api>("api")
    .WaitFor(qdrant)
    .WaitFor(ollama)
    .WithEnvironment("ConnectionStrings__Qdrant", qdrant.GetEndpoint("qdrant-grpc"))
    .WithEnvironment("ConnectionStrings__Ollama", ollama.GetEndpoint("ollama-http"))
    .WithExternalHttpEndpoints();

// 4. Setup the Worker Project (Fixed: Added Key & References)
builder.AddProject<Projects.CrateDiggin_Worker>("worker")
    .WaitFor(qdrant)
    .WaitFor(ollama)
    .WithEnvironment("ConnectionStrings__Qdrant", qdrant.GetEndpoint("qdrant-grpc"))
    .WithEnvironment("ConnectionStrings__Ollama", ollama.GetEndpoint("ollama-http"))
    .WithEnvironment("LastFm__ApiKey", builder.Configuration["LastFm:ApiKey"]);

// 5. Setup the Frontend
builder.AddProject<Projects.CrateDiggin_Web>("web")
    .WithReference(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();