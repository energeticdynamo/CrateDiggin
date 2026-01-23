using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using CrateDiggin.Web.Client.Clients;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddHttpClient<DigginApiClient>(client =>
    client.BaseAddress = new Uri("https+http://api"));


await builder.Build().RunAsync();
