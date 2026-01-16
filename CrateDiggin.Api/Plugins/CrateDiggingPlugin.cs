using System.ComponentModel;
using System.Text.Json;
using CrateDiggin.Api.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;

namespace CrateDiggin.Api.Plugins
{
    public class CrateDiggingPlugin(
    IVectorStoreRecordCollection<Guid, Album> collection,
    ITextEmbeddingGenerationService embeddingService)
    {
        [KernelFunction("search_crates")]
        [Description("Searches for albums in the crate that match a specific vibe, genre, or description.")]
        public async Task<string> SearchCratesAsync([Description("The description of the vibe, mood, or genre to search for")] string vibeQuery)
        {
            // 1. Convert the user's text query into a vector (Embedding)
            // This effectively translates "sad music" into numbers like [0.1, -0.5, ...]
            var queryVector = await embeddingService.GenerateEmbeddingAsync(vibeQuery);

            // 2. Search Qdrant for the closest vectors
            var searchResults = await collection.VectorizedSearchAsync(
                queryVector,
                new VectorSearchOptions { Top = 5}
                );

            //3. Build a list of simple objects
            var matches = new List<object>();

            await foreach (var record in searchResults.Results)
            {
                matches.Add(new
                {
                    title = record.Record.Title,
                    artist = record.Record.Artist,
                    description = record.Record.Description,
                    match_score = Math.Round(record.Score ?? 0, 2)
                });
            }

            // 4. Return as JSON String
            return JsonSerializer.Serialize(matches, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }
    }
}
