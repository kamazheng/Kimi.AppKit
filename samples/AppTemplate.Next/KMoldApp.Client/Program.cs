using Kimi.AppKit.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

// IKConfirm / IKNotify 的 MudBlazor 实现。
builder.Services.AddAppKitDialogs();

await builder.Build().RunAsync();
