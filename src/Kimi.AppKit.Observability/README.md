# Kimi.AppKit.Observability

OpenTelemetry 装配：链路、指标、日志三条信号一次接好，并在上报前抹掉凭据。

隶属 [Kimi.AppKit](https://github.com/kamazheng/Kimi.AppKit)。

## 为什么是独立的包

OTel 带 10 个传递依赖。把它塞进 `Kimi.AppKit.Web`，等于强加给「只想要健康检查和异常处理」的消费方。
与 `Kimi.AppKit.Web` 刻意不引 `Hangfire.PostgreSql` 同理。

## 用法

```csharp
builder.AddAppKitObservability(options =>
{
    options.ServiceNamespace = "acme";
    options.ExcludedPathPrefixes.Add("/hangfire");
});

// ⚠️ 必须在 UseAuthentication() 之后，否则 span 上永远不带用户
app.UseAuthentication();
app.UseAppKitEndUserTag();
```

导出地址走 OTel 标准环境变量，**不在代码里拼**：

| 变量 | 说明 |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | 必填。未配置时**跳过注册**并打印提示，不阻断启动 |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc`（默认）或 `http/protobuf` |
| `OTEL_EXPORTER_OTLP_HEADERS` | 鉴权头，如 `Authorization=Bearer xxx` |

## ⚠️ 脱敏是纵深防御，不是第一道防线

`KSensitiveDataRedactor` 靠**字段名**匹配。业务自定义字段名（如 `userPin`）不登记就会漏，
而且**不会有任何报错**。`CaptureRequestBody` 因此默认关闭——打开之前先想清楚这一点。

自定义字段用 `options.AdditionalSensitiveKeys` 追加。
