using CrateDiggin.Web.Client.Models;
using System.Net.Http.Json;
namespace CrateDiggin.Web.Client.Clients
{
    public class DigginApiClient(HttpClient httpClient)
    {
        public async Task<Album[]> GetAlbumsAsync()
        {
            var response = await httpClient.GetFromJsonAsync<List<dynamic>>("/dig?query=jazz");
            return []; // Placeholder until we share the Album model
        }

        public async Task<string> DigCrates(string vibe)
        {
            var response = await httpClient.GetStringAsync($"/dig?query={vibe}");
            return response;
        }

        public async Task<List<Album>> SearchAsync(string query)
        {
            // We will call a new endpoint "/api/search" that returns raw album data with covers
            var response = await httpClient.GetFromJsonAsync<List<Album>>($"/api/search?query={query}");
            return response ?? [];
        }
    }
}
