using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ServerTest.Protocol
{
    /// <summary>
    /// 请求 ID 解析器：用于中间件阶段补充 reqId
    /// </summary>
    public static class ProtocolRequestIdResolver
    {
        private static readonly string[] HeaderKeys =
        {
            "X-Req-Id",
            "X-Request-Id"
        };

        public static async Task<string?> ResolveAsync(HttpContext context, int maxBodyBytes)
        {
            var reqId = ProtocolContext.GetReqId(context);
            if (!string.IsNullOrWhiteSpace(reqId))
            {
                return reqId;
            }

            foreach (var key in HeaderKeys)
            {
                if (context.Request.Headers.TryGetValue(key, out var headerValue))
                {
                    reqId = headerValue.ToString();
                    if (!string.IsNullOrWhiteSpace(reqId))
                    {
                        return reqId;
                    }
                }
            }

            if (context.Request.Query.TryGetValue("reqId", out var queryValue))
            {
                reqId = queryValue.ToString();
                if (!string.IsNullOrWhiteSpace(reqId))
                {
                    return reqId;
                }
            }

            if (!IsJsonContentType(context.Request.ContentType))
            {
                return null;
            }

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value == 0)
            {
                return null;
            }

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > maxBodyBytes)
            {
                return null;
            }

            context.Request.EnableBuffering();
            string? body;
            try
            {
                body = await ReadBodyAsync(context.Request.Body, maxBodyBytes, context.RequestAborted).ConfigureAwait(false);
            }
            finally
            {
                context.Request.Body.Position = 0;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return TryGetReqId(doc.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static async Task<string?> ReadBodyAsync(Stream body, int maxBodyBytes, CancellationToken ct)
        {
            // 限制读取大小，避免大请求占用内存
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

        private static bool IsJsonContentType(string? contentType)
        {
            return !string.IsNullOrWhiteSpace(contentType)
                && contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetReqId(JsonElement root)
        {
            if (TryGetProperty(root, "reqId", out var value)
                || TryGetProperty(root, "ReqId", out value)
                || TryGetProperty(root, "reqID", out value)
                || TryGetProperty(root, "reqid", out value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    var reqId = value.GetString();
                    return string.IsNullOrWhiteSpace(reqId) ? null : reqId;
                }

                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                {
                    return number.ToString();
                }
            }

            return null;
        }

        private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
        {
            if (root.TryGetProperty(name, out value))
            {
                return true;
            }

            value = default;
            return false;
        }
    }
}
