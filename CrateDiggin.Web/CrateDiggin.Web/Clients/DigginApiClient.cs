using CrateDiggin.Api.Models; // We'll need to copy the Album model here too, or reference API

namespace CrateDiggin.Web.Clients
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
    }
}
