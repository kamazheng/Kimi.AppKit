using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kimi.AppKit.Web.Email;

/// <summary>邮件发送的注册入口。</summary>
public static class KEmailServiceCollectionExtensions
{
    /// <summary>
    /// 注册基于 MailKit 的 <see cref="IKEmailSender"/>，从 <see cref="KEmailOptions.SectionName"/> 读配置。
    /// </summary>
    /// <remarks>
    /// ⚠️ 未配置 SMTP 时**不会**阻止应用启动——校验发生在真正发送的那一刻。
    /// 一个从来不发邮件的部署不该因为没配 SMTP 就起不来。
    ///
    /// ⚠️ SMTP 密码走 user-secrets 或环境变量 <c>Email__Password</c>，不要写进 appsettings。
    /// </remarks>
    public static IServiceCollection AddAppKitEmail(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KEmailOptions>(configuration.GetSection(KEmailOptions.SectionName));
        services.TryAddSingleton<IKEmailSender, MailKitEmailSender>();

        return services;
    }
}
