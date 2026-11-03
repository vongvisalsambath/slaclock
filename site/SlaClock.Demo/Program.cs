using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SlaClock.Demo;

// The page is plain HTML; only the "Try it" section is a .NET component, running the real SlaClock library.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Demo>("#demo");
await builder.Build().RunAsync();
