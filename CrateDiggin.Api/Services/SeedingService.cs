using CrateDiggin.Api.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Embeddings;
using System.Security.Cryptography;

namespace CrateDiggin.Api.Services
{
    public class SeedingService(IVectorStoreRecordCollection<Guid, Album> collection, 
        ITextEmbeddingGenerationService embeddingService)
    {
        public async Task<string> SeedCratesAsync()
        {
            await collection.CreateCollectionIfNotExistsAsync();

            var starterPack = new List<Album>
            {
                new() { Title = "Illmatic", Artist = "Nas", Description = "Raw 90s boom-bap hip-hop, gritty new york streets, poetic lyricism, jazz samples, urban storytelling" },
                new() { Title = "Unknown Pleasures", Artist = "Joy Division", Description = "Post-punk, gothic rock, melancholic, dark atmosphere, industrial textures, bass-heavy, minimalist" },
                new() { Title = "Dummy", Artist = "Portishead", Description = "Trip-hop, cinematic, noir, bristol sound, haunting female vocals, spy movie vibes, downtempo, experimental" },
                new() { Title = "Homework", Artist = "Daft Punk", Description = "French house, raw techno, chicago house influence, lo-fi dance, repetitive beats, funk samples, basement party vibes" },
                new() { Title = "Ride the Lightning", Artist = "Metallica", Description = "Thrash metal, aggressive, fast tempo, complex guitar solos, electric, angry, 80s metal" },
                new() { Title = "Kind of Blue", Artist = "Miles Davis", Description = "Cool jazz, modal jazz, relaxing, sophisticated, trumpet, saxophone, smoke-filled lounge vibe, masterpiece" },
                new() { Title = "Rumours", Artist = "Fleetwood Mac", Description = "Soft rock, pop rock, emotional, relationship drama, harmonies, acoustic guitar, california 70s vibes" },
                new() { Title = "Currents", Artist = "Tame Impala", Description = "Psychedelic pop, synth-pop, dreamy, hazy, psychedelic rock, introspective, modern indie" },
                new() { Title = "Selected Ambient Works 85-92", Artist = "Aphex Twin", Description = "Ambient techno, idm, electronic, atmospheric, ethereal, warm analog synths, relaxing focus music" },
                new() { Title = "The Low End Theory", Artist = "A Tribe Called Quest", Description = "Jazz rap, conscious hip-hop, groovy basslines, afrocentric, positive vibes, 90s classic" }
            };

            int count = 0;
            foreach (var album in starterPack)
            {
                // Assign a new ID
                album.Id = GenerateDeterministicGuid(album.Artist, album.Title);

                // Generate the "Vibe Vector" (The most important part!)
                // We verify if the vector is empty before generating to save time/compute
                if (album.Vector.IsEmpty)
                {
                    Console.WriteLine($"[Seeding] Generating vibes for: {album.Title}...");
                    album.Vector = await embeddingService.GenerateEmbeddingAsync(album.Description);
                }

                // 4. Save to Qdrant
                await collection.UpsertAsync(album);
                count++;
            }

            return $"Successfully seeded {count} albums into the crates!";
        }

        private static Guid GenerateDeterministicGuid(string artist, string title)
        {
            var key = $"{artist.ToLowerInvariant()}|{title.ToLowerInvariant()}";
            var md5 = MD5.Create();
            var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
            return new Guid(hash);
        }
    }
}
