using CrateDiggin.Api.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using System.ComponentModel;
using System.Text.Json;

namespace CrateDiggin.Api.Plugins
{
    public class CrateDiggingPlugin(
    IVectorStoreRecordCollection<Guid, Album> collection,
    ITextEmbeddingGenerationService embeddingService,
    IChatCompletionService chatService)
    {
        [KernelFunction("dig_crate")]
        [Description("Searches for albums and organizes them into Vibe Bins.")]
        public async Task<string> DigCrateAsync([Description("The user's vibe description")] string query)
        {
            // --- 1. The Retrieval (RAG) ---
            // Convert query to vector and search Qdrant
            var queryVector = await embeddingService.GenerateEmbeddingAsync(query);
            var searchResults = await collection.VectorizedSearchAsync(queryVector, new VectorSearchOptions { Top = 6 });

            var foundAlbums = new List<Album>();
            await foreach (var result in searchResults.Results)
            {
                foundAlbums.Add(result.Record);
            }

            if (foundAlbums.Count == 0)
                return JsonSerializer.Serialize(new { message = "The crates are empty. Try adding more albums!" });

            // --- 2. The Reasoning (The Clerk) ---
            // We create a prompt that gives the LLM the data and tells it to be a Clerk
            var chatHistory = new ChatHistory();

            chatHistory.AddSystemMessage(
            "You are 'Crate Digga', a knowledgeable, cool, and slightly opinionated record store clerk. " +
            "You don't just give lists; you curate experiences. " +
            "Analyze the albums provided and group them into 2 distinct 'Vibe Bins' (creative categories). " +
            "Output the result as a clean JSON object.");

            // Serialize the albums we found so the LLM can read them
            var albumsJson = JsonSerializer.Serialize(foundAlbums.Select(a => new { a.Title, a.Artist, a.Description }));

            chatHistory.AddUserMessage(
            $"User is looking for: '{query}'.\n" +
            $"I found these albums in the database: {albumsJson}.\n\n" +
            "Please group these into 2 creative 'Vibe Bins'. Return ONLY JSON in this format:\n" +
            "{\n" +
            "  \"clerk_comment\": \"Your short, cool intro to the user.\",\n" +
            "  \"bins\": [\n" +
            "    { \"bin_name\": \"Creative Name 1\", \"description\": \"Why these go together\", \"albums\": [ \"Album Title\", ... ] },\n" +
            "    { \"bin_name\": \"Creative Name 2\", \"description\": \"Why these go together\", \"albums\": [ \"Album Title\", ... ] }\n" +
            "  ]\n" +
            "}");

            // --- 3. The Response ---
            var response = await chatService.GetChatMessageContentAsync(chatHistory);

            // Clean up the response (Mistral sometimes adds markdown '''json blocks)
            var cleanJson = response.Content!.Replace("```json", "").Replace("```", "").Trim();

            return cleanJson;
        }

    }
}
