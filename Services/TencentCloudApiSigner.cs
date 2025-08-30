using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;

namespace CallREC_Scribe.Services
{
    public static class TencentCloudApiSigner
    {
        private const string ALGORITHM = "TC3-HMAC-SHA256";

        public static async Task AddApiSignatureHeadersAsync(HttpClient client, HttpRequestMessage request, string secretId, string secretKey, string service, string region, string action, string version)
        {
            string host = "asr.tencentcloudapi.com";
            DateTime requestTime = DateTime.UtcNow;
            string timestamp = new DateTimeOffset(requestTime).ToUnixTimeSeconds().ToString();
            string date = requestTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 1. 拼接规范请求串 (Canonical Request)
            string httpRequestMethod = request.Method.Method.ToUpper();
            string canonicalUri = "/";
            string canonicalQueryString = ""; // 本次使用的两个API都是POST请求，查询参数为空

            // 提取并排序头部
            var headersToSign = new SortedDictionary<string, string>
        {
            { "content-type", "application/json; charset=utf-8" },
            { "host", host }
        };
            string canonicalHeaders = string.Join("", headersToSign.Select(kvp => $"{kvp.Key.ToLower()}:{kvp.Value.Trim()}\n"));
            string signedHeaders = string.Join(";", headersToSign.Keys.Select(k => k.ToLower()));

            // 计算Payload的SHA256哈希
            string requestPayload = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            string hashedRequestPayload = ToHexString(SHA256Hash(Encoding.UTF8.GetBytes(requestPayload))).ToLower();

            string canonicalRequest =
                $"{httpRequestMethod}\n" +
                $"{canonicalUri}\n" +
                $"{canonicalQueryString}\n" +
                $"{canonicalHeaders}\n" +
                $"{signedHeaders}\n" +
                $"{hashedRequestPayload}";

            // 2. 拼接待签名字符串 (String to Sign)
            string credentialScope = $"{date}/{service}/tc3_request";
            string hashedCanonicalRequest = ToHexString(SHA256Hash(Encoding.UTF8.GetBytes(canonicalRequest))).ToLower();
            string stringToSign =
                $"{ALGORITHM}\n" +
                $"{timestamp}\n" +
                $"{credentialScope}\n" +
                $"{hashedCanonicalRequest}";

            // 3. 计算签名 (Signature)
            byte[] secretDate = HmacSHA256(Encoding.UTF8.GetBytes($"TC3{secretKey}"), Encoding.UTF8.GetBytes(date));
            byte[] secretService = HmacSHA256(secretDate, Encoding.UTF8.GetBytes(service));
            byte[] secretSigning = HmacSHA256(secretService, Encoding.UTF8.GetBytes("tc3_request"));
            byte[] signature = HmacSHA256(secretSigning, Encoding.UTF8.GetBytes(stringToSign));
            string signatureHex = ToHexString(signature).ToLower();

            // 4. 拼接 Authorization Header
            string authorization =
                $"{ALGORITHM} " +
                $"Credential={secretId}/{credentialScope}, " +
                $"SignedHeaders={signedHeaders}, " +
                $"Signature={signatureHex}";

            // 5. 添加到请求头
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
            request.Headers.TryAddWithoutValidation("Host", host);
            request.Headers.TryAddWithoutValidation("X-TC-Action", action);
            request.Headers.TryAddWithoutValidation("X-TC-Version", version);
            request.Headers.TryAddWithoutValidation("X-TC-Timestamp", timestamp);
            request.Headers.TryAddWithoutValidation("X-TC-Region", region);
        }

        private static byte[] HmacSHA256(byte[] key, byte[] msg)
        {
            using (var hmac = new HMACSHA256(key))
            {
                return hmac.ComputeHash(msg);
            }
        }

        private static byte[] SHA256Hash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                return sha256.ComputeHash(data);
            }
        }

        private static string ToHexString(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }
    }
}
