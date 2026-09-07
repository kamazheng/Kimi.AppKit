namespace Kimi.AppKit.Core.Contracts;

/// <summary>网络准入的判定结果。</summary>
public enum KNetworkGateResult
{
    /// <summary>来源在白名单网段内。</summary>
    Allowed,

    /// <summary>白名单为空——**拒绝**。方向刻意如此：忘了配等于更严。</summary>
    DeniedNotConfigured,

    /// <summary>取不到来源 IP。</summary>
    DeniedUnknownAddress,

    /// <summary>来源 IP 不在任何白名单网段内。</summary>
    DeniedOutOfRange,
}

// ⚠️ 这个枚举放在 Core 而不是 Web，是为了让**认证页组件**（Kimi.AppKit.Components，
//    一个 Razor 类库）能引用它。判定逻辑仍在 Web 的 KNetworkGate 里——
//    组件只接收判定**结果**作为参数，不自己去判，也就不必依赖 Web 包。
//    反过来让 Components 引用 Web 会造成依赖倒置：Web 是服务端装配包，
//    而 Components 要能被 WASM 客户端引用，那里根本没有 ASP.NET Core。
