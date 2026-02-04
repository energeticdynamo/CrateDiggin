using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CrateDiggin.Web.Client.Clients;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});
builder.Services.AddScoped<DigginApiClient>();


await builder.Build().RunAsync();
