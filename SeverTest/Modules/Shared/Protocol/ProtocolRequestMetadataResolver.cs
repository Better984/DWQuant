using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ServerTest.Protocol
{
    /// <summary>
    /// 协议请求元数据解析器：用于在中间件阶段补充 type / reqId。
    /// </summary>
    public static class ProtocolRequestMetadataResolver
    {
        private static readonly string[] HeaderReqIdKeys =
        {
            "X-Req-Id",
            "X-Request-Id"
        };

        public static async Task<ProtocolRequestMetadata> ResolveAsync(HttpContext context, int maxBodyBytes)
        {
            var reqId = ProtocolContext.GetReqId(context);
            var type = ProtocolContext.GetType(context);
            if (!string.IsNullOrWhiteSpace(reqId) && !string.IsNullOrWhiteSpace(type))
            {
                return new ProtocolRequestMetadata(type, reqId);
            }

            reqId ??= ResolveReqIdFromHeaders(context);
            type ??= ResolveTypeFromQuery(context);

            if (!string.IsNullOrWhiteSpace(reqId) && !string.IsNullOrWhiteSpace(type))
            {
                return new ProtocolRequestMetadata(type, reqId);
            }

            if (!CanReadJsonBody(context, maxBodyBytes))
            {
                return new ProtocolRequestMetadata(type, reqId);
            }

            context.Request.EnableBuffering();
            string? body;
            try
            {
                body = await ReadBodyAsync(context.Request.Body, maxBodyBytes, context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                if (context.Request.Body.CanSeek)
                {
                    context.Request.Body.Position = 0;
                }
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return new ProtocolRequestMetadata(type, reqId);
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return new ProtocolRequestMetadata(type, reqId);
                }

                type ??= TryGetString(doc.RootElement, "type");
                reqId ??= TryGetString(doc.RootElement, "reqId");
            }
            catch (JsonException)
            {
                // 忽略解析失败，保持兜底逻辑最小化。
            }

            return new ProtocolRequestMetadata(type, reqId);
        }

        private static bool CanReadJsonBody(HttpContext context, int maxBodyBytes)
        {
            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value == 0)
            {
                return false;
            }

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > maxBodyBytes)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(context.Request.ContentType)
                && context.Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }

        private static string? ResolveReqIdFromHeaders(HttpContext context)
        {
            foreach (var key in HeaderReqIdKeys)
            {
                if (!context.Request.Headers.TryGetValue(key, out var value))
                {
                    continue;
                }

                var reqId = value.ToString();
                if (!string.IsNullOrWhiteSpace(reqId))
                {
                    return reqId;
                }
            }

            if (context.Request.Query.TryGetValue("reqId", out var queryValue))
            {
                var reqId = queryValue.ToString();
                if (!string.IsNullOrWhiteSpace(reqId))
                {
                    return reqId;
                }
            }

            return null;
        }

        private static string? ResolveTypeFromQuery(HttpContext context)
        {
            if (!context.Request.Query.TryGetValue("type", out var queryValue))
            {
                return null;
            }

            var type = queryValue.ToString();
            return string.IsNullOrWhiteSpace(type) ? null : type;
        }

        private static async Task<string?> ReadBodyAsync(Stream body, int maxBodyBytes, CancellationToken ct)
        {
            var buffer = new byte[8192];
            var total = 0;
            using var stream = new MemoryStream();

            while (true)
            {
                var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBodyBytes)
                {
                    return null;
                }

                stream.Write(buffer, 0, read);
            }

            if (stream.Length == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static string? TryGetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
                JsonValueKind.Number when value.TryGetInt64(out var number) => number.ToString(),
                _ => null
            };
        }
    }

    public sealed record ProtocolRequestMetadata(string? Type, string? ReqId);
}
