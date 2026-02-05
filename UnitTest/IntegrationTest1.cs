using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.Testing;

namespace UnitTest.Tests
{
    public class IntegrationTest1
    {
        [Fact]
        public async Task ApiService_ReturnsOk_OnVerifyBrain()
        {
            var appHost = await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.CrateDiggin_AppHost>();

            await using var app = await appHost.BuildAsync();
            await app.StartAsync();

            var httpClient = app.CreateHttpClient("api");

            var response = await httpClient.GetAsync("/verify-brain");

            response.EnsureSuccessStatusCode();
        }
    }
}
