using Uhp.FixtureServer;
using Uhp.Server;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddAuthentication("Fixture").AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, FixtureAuthenticationHandler>("Fixture", null);
builder.Services.AddUhpServer<FixtureBackend>(options => options.AuthenticationScheme = "Fixture");

var app = builder.Build();
app.MapUhp("/api/harness");
app.MapGet("/health", HandleHealth);
await app.RunAsync();

static Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult HandleHealth() => TypedResults.Text("ok");
