namespace Kimi.AppKit.Web.Authentication;

/// <summary>二维码登录的配置。</summary>
public sealed class KQrLoginOptions
{
    /// <summary>配置节名。</summary>
    public const string SectionName = "Auth:QrLogin";

    /// <summary>
    /// 二维码凭据的有效期。
    /// </summary>
    /// <remarks>
    /// 【⚠️ 这张卡片就是密码本身】密文里装着账号和明文密码，印在纸上。
    /// 有效期是这个方案唯一的止损手段——过了就作废，纸片捡到也没用。
    ///
    /// 默认 8 小时 ≈ 一个班次：当班有效，下班作废，重新打印只是点一次按钮。
    /// **调长之前先想清楚「这张纸落在别人手里能用多久」。**
    ///
    /// 前身完全没有这个概念：密文结构只有 {账号, 密码, 版本号}，
    /// DataProtection 的密文本身也不带过期，于是一张打印出来的卡片**永久有效**——
    /// 员工离职、密码轮换之后那张纸依然能登录。
    ///
    /// 配置写法（<c>TimeSpan</c> 认 <c>[d.]hh:mm:ss</c>）：
    /// <code>
    /// "Auth": { "QrLogin": { "Lifetime": "08:00:00" } }
    /// </code>
    /// </remarks>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(8);
}
