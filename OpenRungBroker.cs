using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace OpenRungClashUploader;

internal static class OpenRungBroker
{
    private const string SignatureHeader = "X-OpenRung-Relays-Signature";
    private const string ActiveKeyHex = "176c03cbc70833285abcea75f2a0e137bd687629142408c22806a86308bd4974";
    private const string StandbyKeyHex = "5b2698cfa7a796c671a30aabd5475d55095b91464221f051837eb8fe01f36ea2";
    private static readonly string[] BrokerBases =
    [
        "https://broker.openrung.org/",
        "https://d2r7mdpyevvs1m.cloudfront.net/",
        "https://cdn-edge-cxdnhsg2aadmaubj.z02.azurefd.net/"
    ];

    internal sealed record FetchResult(string Yaml, int TotalCount, int CompatibleCount, DateTimeOffset NotAfter);

    public static async Task<FetchResult> FetchMihomoYamlAsync(string clientId, int limit)
    {
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) };
        var sessionId = Guid.NewGuid().ToString();
        var errors = new List<string>();

        foreach (var brokerBase in BrokerBases)
        {
            try
            {
                var url = $"{brokerBase.TrimEnd('/')}/api/v1/relays?limit={limit}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
                request.Headers.Pragma.ParseAdd("no-cache");
                request.Headers.TryAddWithoutValidation("X-OpenRung-App-Version", "1.0.0");
                request.Headers.TryAddWithoutValidation("X-OpenRung-Desktop", "windows");
                if (!string.IsNullOrWhiteSpace(clientId))
                {
                    request.Headers.TryAddWithoutValidation("X-OpenRung-Client-ID", clientId);
                    request.Headers.TryAddWithoutValidation("X-OpenRung-Session-ID", sessionId);
                }

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    errors.Add($"{new Uri(brokerBase).Host}: HTTP {(int)response.StatusCode}");
                    continue;
                }
                if (!response.Headers.TryGetValues(SignatureHeader, out var signatureValues))
                {
                    errors.Add($"{new Uri(brokerBase).Host}: 缺少签名头");
                    continue;
                }
                var body = await ReadLimitedBodyAsync(response.Content, 4 * 1024 * 1024);
                var signature = signatureValues.FirstOrDefault() ?? "";
                if (!VerifySignedBody(signature, body, limit, out var envelope, out var verifyError))
                {
                    errors.Add($"{new Uri(brokerBase).Host}: {verifyError}");
                    continue;
                }

                var proxies = envelope.Relays
                    .Where(IsMihomoCompatible)
                    .Select(ToProxy)
                    .ToList();
                if (proxies.Count == 0)
                    throw new InvalidOperationException($"Broker 返回 {envelope.Relays.Count} 个候选，但没有可转换为标准 VLESS + REALITY 的节点（特殊传输已跳过）。");
                return new FetchResult(Render(proxies), envelope.Relays.Count, proxies.Count, envelope.NotAfter);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or CryptographicException)
            {
                errors.Add($"{new Uri(brokerBase).Host}: {DescribeException(ex)}");
            }
        }

        throw new HttpRequestException("所有 OpenRung Broker 前端都未返回有效的已签名节点目录。 " + string.Join("；", errors));
    }

    private static string DescribeException(Exception exception)
    {
        var details = new List<string>();
        for (var current = exception; current is not null && details.Count < 5; current = current.InnerException!)
            details.Add($"{current.GetType().Name}: {current.Message}");
        return string.Join(" -> ", details);
    }

    private static bool VerifySignedBody(string header, byte[] body, int requestedLimit, out RelayListDto list, out string error)
    {
        list = new RelayListDto();
        error = "";
        var fields = header.Split(';');
        if (fields.Length != 3 || fields[0] != "ed25519") { error = "签名头格式错误"; return false; }
        byte[] signature;
        try { signature = Convert.FromBase64String(fields[2]); }
        catch (FormatException) { error = "签名不是有效 Base64"; return false; }
        if (signature.Length != 64) { error = "Ed25519 签名长度无效"; return false; }

        var keys = new[] { Convert.FromHexString(ActiveKeyHex), Convert.FromHexString(StandbyKeyHex) };
        var verified = false;
        foreach (var key in keys)
        {
            var keyId = Convert.ToHexString(SHA256.HashData(key).AsSpan(0, 8)).ToLowerInvariant();
            if (keyId != fields[1]) continue;
            var publicKey = new Ed25519PublicKeyParameters(key, 0);
            if (publicKey.Verify(Ed25519.Algorithm.Ed25519, null, body, signature)) { verified = true; break; }
        }
        if (!verified)
        {
            foreach (var key in keys)
            {
                var publicKey = new Ed25519PublicKeyParameters(key, 0);
                if (publicKey.Verify(Ed25519.Algorithm.Ed25519, null, body, signature)) { verified = true; break; }
            }
        }
        if (!verified) { error = "目录签名无法通过 OpenRung 固定公钥验证"; return false; }

        try { list = JsonSerializer.Deserialize<RelayListDto>(body) ?? throw new JsonException("空目录"); }
        catch (JsonException ex) { error = "签名目录 JSON 无效：" + ex.Message; return false; }
        if (!string.Equals(list.Channel, "api", StringComparison.Ordinal)) { error = "签名目录 channel 不匹配"; return false; }
        if (list.Limit != requestedLimit) { error = "签名目录 limit 与请求不匹配"; return false; }
        if (string.IsNullOrWhiteSpace(list.KeyId) || list.ServerTime == default) { error = "签名目录缺少 key_id 或 server_time"; return false; }
        if (list.NotAfter == default) { error = "签名目录没有有效期"; return false; }
        if (list.NotAfter < DateTimeOffset.UtcNow.AddMinutes(-5)) { error = "签名目录已过期"; return false; }
        if (list.Relays is null) { error = "签名目录缺少 relays 数组"; return false; }
        if (list.Count != list.Relays.Count) { error = "签名目录 count 与 relays 数量不匹配"; return false; }
        return true;
    }

    private static async Task<byte[]> ReadLimitedBodyAsync(HttpContent content, int maxBytes)
    {
        var contentLength = content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
            throw new InvalidDataException($"Broker 响应超过 {maxBytes} 字节。");
        await using var stream = await content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0) break;
            if (buffer.Length + read > maxBytes) throw new InvalidDataException($"Broker 响应超过 {maxBytes} 字节。");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool IsMihomoCompatible(RelayDto relay) =>
        string.Equals(relay.Protocol, "vless-reality-vision", StringComparison.Ordinal) &&
        (string.IsNullOrEmpty(relay.Transport) || string.Equals(relay.Transport, "direct", StringComparison.Ordinal)) &&
        !string.IsNullOrWhiteSpace(relay.PublicHost) && relay.PublicPort is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(relay.ClientId) &&
        !string.IsNullOrWhiteSpace(relay.RealityPublicKey) &&
        !string.IsNullOrWhiteSpace(relay.ServerName) &&
        relay.ExpiresAt > DateTimeOffset.UtcNow;

    private static ProxyDto ToProxy(RelayDto relay)
    {
        var suffix = string.IsNullOrWhiteSpace(relay.CountryCode) ? relay.Label : relay.CountryCode;
        var name = string.IsNullOrWhiteSpace(suffix) ? relay.Id : $"{relay.Label} ({suffix})";
        if (string.IsNullOrWhiteSpace(relay.Label)) name = string.IsNullOrWhiteSpace(relay.CountryCode) ? relay.Id : relay.CountryCode;
        return new ProxyDto(name, relay.PublicHost, relay.PublicPort, relay.ClientId, relay.ServerName,
            relay.RealityPublicKey, relay.ShortId, string.IsNullOrWhiteSpace(relay.Flow) ? "xtls-rprx-vision" : relay.Flow);
    }

    private static string Render(List<ProxyDto> proxies)
    {
        var sb = new StringBuilder("# OpenRung signed candidate directory converted to Mihomo\nproxies:\n");
        foreach (var p in proxies)
        {
            sb.Append("  - name: ").Append(Q(p.Name)).Append("\n    type: vless\n    server: ").Append(Q(p.Server))
              .Append("\n    port: ").Append(p.Port.ToString(CultureInfo.InvariantCulture))
              .Append("\n    uuid: ").Append(Q(p.Uuid)).Append("\n    network: tcp\n    udp: true\n    tls: true\n    servername: ").Append(Q(p.ServerName))
              .Append("\n    client-fingerprint: chrome\n    flow: ").Append(Q(p.Flow))
              .Append("\n    reality-opts:\n      public-key: ").Append(Q(p.PublicKey))
              .Append("\n      short-id: ").Append(Q(p.ShortId)).Append('\n');
        }
        sb.Append("\nproxy-groups:\n  - name: \"OpenRung\"\n    type: select\n    proxies:\n");
        foreach (var p in proxies) sb.Append("      - ").Append(Q(p.Name)).Append('\n');
        sb.Append("\nrules:\n")
            .Append("  # OpenRung-style LAN bypass: private/local addresses go direct.\n")
            .Append("  - IP-CIDR,0.0.0.0/8,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR,10.0.0.0/8,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR,127.0.0.0/8,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR,169.254.0.0/16,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR,172.16.0.0/12,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR,192.168.0.0/16,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR6,::1/128,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR6,fc00::/7,DIRECT,no-resolve\n")
            .Append("  - IP-CIDR6,fe80::/10,DIRECT,no-resolve\n")
            .Append("  - GEOSITE,CN,DIRECT\n")
            .Append("  - GEOIP,CN,DIRECT,no-resolve\n")
            .Append("  - MATCH,OpenRung\n");
        return sb.ToString();
    }

    private static string Q(string value) => JsonSerializer.Serialize(value);

    private sealed class RelayListDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("channel")] public string Channel { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("count")] public int Count { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("limit")] public int Limit { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("server_time")] public DateTimeOffset ServerTime { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("not_after")] public DateTimeOffset NotAfter { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("key_id")] public string KeyId { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("relays")] public List<RelayDto> Relays { get; set; } = [];
    }

    private sealed class RelayDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")] public string Id { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("label")] public string Label { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("public_host")] public string PublicHost { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("public_port")] public int PublicPort { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("country_code")] public string CountryCode { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("protocol")] public string Protocol { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("client_id")] public string ClientId { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("reality_public_key")] public string RealityPublicKey { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("short_id")] public string ShortId { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("server_name")] public string ServerName { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("flow")] public string Flow { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("transport")] public string Transport { get; set; } = "";
        [System.Text.Json.Serialization.JsonPropertyName("expires_at")] public DateTimeOffset ExpiresAt { get; set; }
    }

    private sealed record ProxyDto(string Name, string Server, int Port, string Uuid, string ServerName, string PublicKey, string ShortId, string Flow);
}
