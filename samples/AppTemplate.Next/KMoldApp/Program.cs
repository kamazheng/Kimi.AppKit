using Kimi.AppKit.Components;
using KMoldApp.Client.Pages;
using KMoldApp.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// ⚠️ MudBlazor 与对话框服务**两端都要注册**：WASM 端给真正跑在浏览器里的组件用；
//    服务端在**预渲染**含这些组件的页面时也要能解析它们。
//    少了服务端这份，首页直接 500：
//    "Cannot provide a value for property 'PopoverService' on type 'MudBlazor.MudPopoverProvider'"。
//    ⚠️ 这不是「客户端注册就够了」——预渲染跑在服务端进程里，用的是服务端的容器。
builder.Services.AddMudServices();
builder.Services.AddAppKitDialogs();

var app = builder.Build();

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
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(KMoldApp.Client._Imports).Assembly);

app.Run();
