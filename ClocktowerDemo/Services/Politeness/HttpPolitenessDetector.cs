using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClocktowerDemo.Domain;

namespace ClocktowerDemo.Services.Politeness
{
    public sealed class HttpPolitenessDetector : IPolitenessDetector
    {
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _http;
        private readonly string _endpoint;

        public HttpPolitenessDetector(HttpClient http, string baseUrl)
        {
            _http = http;
            _endpoint = baseUrl.TrimEnd('/') + "/classify";
        }

        public async Task<PolitenessResult> ClassifyAsync(string text, CancellationToken ct)
        {
            // tiny retry (2 tries) so transient 500/timeout doesn’t kill the turn
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var resp = await _http.PostAsJsonAsync(_endpoint, new { text }, JsonOpts, ct);
                    resp.EnsureSuccessStatusCode();

                    var json = await resp.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var label = root.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String
                        ? l.GetString()!
                        : "neutral";
                    var conf = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number
                        ? c.GetDouble()
                        : 0.5;

                    return new PolitenessResult(label, conf);
                }
                catch when (attempt == 0)
                {
                    await Task.Delay(120, ct); // brief backoff then try once more
                }
            }

            // graceful fallback so the game keeps running
            return new PolitenessResult("neutral", 0.34);
        }
    }
}
