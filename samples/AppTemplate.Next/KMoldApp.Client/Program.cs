using Kimi.AppKit.Components;
using Kimi.AppKit.Crud.Http;
using KMoldApp.Shared.Constants;
using KMoldApp.Shared.Entities;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

// IKConfirm / IKNotify 的 MudBlazor 实现。
builder.Services.AddAppKitDialogs();

// ⚠️ 客户端也要注册同一套策略：<AuthorizeView> 与页面上的 [Authorize(Policy=...)]
//    在 WASM 里求值，缺了策略会抛「The AuthorizationPolicy named ... was not found」
//    并让整页白屏。策略定义在 Shared，两端共用一份，避免漂移。
builder.Services.AddAuthorizationCore(options => options.AddAppPolicies());
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, Kimi.AppKit.Components.Auth.KPersistentAuthenticationStateProvider>();

// ⚠️ BaseAddress 必须指向应用根：KHttpCrudDataSource 用相对路径拼端点。
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// CRUD 数据源走 HTTP，对接服务端的 MapCrudEndpoints<Setting>()。
// ⚠️ 注册它不等于开放了数据——服务端端点出厂即 RequireAuthorization()。
builder.Services.AddHttpCrudDataSource<SettingDto>("api/crud/setting");

// 企业标识：读服务端随首屏送来的持久化状态，客户端不打身份服务（跨域 + 多一次往返）。
builder.Services.AddScoped<Kimi.AppKit.Core.Contracts.IKBrandingSource, Kimi.AppKit.Components.Auth.KClientBranding>();

// <KEnvChip /> 在 WASM 接管后会重新渲染一次，用的是这个容器，
// 服务端那份 AddAppKitEnvironment() 管不到这里。两端都要注册，缺一个就整页崩。
builder.Services.AddAppKitClientEnvironment();

await builder.Build().RunAsync();
