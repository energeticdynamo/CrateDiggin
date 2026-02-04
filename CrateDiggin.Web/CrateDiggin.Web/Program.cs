using CrateDiggin.Web.Client.Clients;
using CrateDiggin.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddHttpClient<DigginApiClient>(client =>
    client.BaseAddress = new("https+http://api"));

// Named HttpClient for proxying with service discovery
builder.Services.AddHttpClient("ApiProxy", client =>
    client.BaseAddress = new Uri("https+http://api"))
    .AddServiceDiscovery();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

// Simple proxy for /api/* requests
app.Map("/api/{**path}", async (string? path, HttpContext context, IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("ApiProxy");

    var requestUri = $"/api/{path}{context.Request.QueryString}";

    using var response = await client.GetAsync(requestUri);

    context.Response.StatusCode = (int)response.StatusCode;
    context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

    await response.Content.CopyToAsync(context.Response.Body);
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(CrateDiggin.Web.Client._Imports).Assembly);

app.Run();