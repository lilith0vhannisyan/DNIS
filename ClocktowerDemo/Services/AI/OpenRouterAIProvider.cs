using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClocktowerDemo.Configuration;
using ClocktowerDemo.Domain;
using System.Linq;

namespace ClocktowerDemo.Services.AI
{
    public class OpenRouterAIProvider : IAIProvider
    {
        private readonly HttpClient _http;
        private readonly List<ChatMessage> _history;

        private const int MaxTokens = 256;

        //private static readonly string[] FallbackModels ={
        //    "deepseek/deepseek-chat-v3-0324:free"
        //};

        public OpenRouterAIProvider(HttpClient http, List<ChatMessage> sharedHistory)
        {
            _http = http; _http.Timeout = AppCfg.HttpTimeout;
            _history = sharedHistory;
        }

        public Task<JsonElement> RoleplayAsync(object payload, CancellationToken ct)
            => PostAsync(@"You are role-playing as the current NPC. Return ONLY JSON: { ""roleplay"": ""..."" }.
                        Context you may use comes in the user payload:
                        - player_utterance: what the player just said
                        - facts.npc_context: { name?, core_attitude?, can_initiate?, emotion, trust, hint?, hint_item?, location }
                        - facts.*: concrete game facts pulled from the ontology (items, uses, locations, puzzle rules, etc.)
                        - facts_missing: topics we could not resolve from the ontology
                        Rules:
                        1) Speak as the NPC in first person. Do NOT prefix with your name, do NOT add quotes, do NOT add brackets.
                        2) One concise line (<= 20 words). No extra narration.
                        3) Use ONLY `facts` and the `player_utterance`. If the player asks about anything in `facts_missing`, briefly say you don't know.
                        4) Do NOT invent new places, items, factions, or lore. Stick strictly to provided facts.
                        5) Match tone to facts.npc_context.emotion:
                           - reserved → brief, measured
                           - friendly → warm, helpful
                           - angry → curt, a bit sharp (but not abusive)
                        6) If the player greets or is vague, you MAY ask ONE short, natural follow-up. 
                           If facts.npc_context.can_initiate is false, avoid follow-ups.
                        7) You may use facts.npc_context.hint when relevant, but don't dump everything—answer only what was asked.", payload, ct);

        private async Task<JsonElement> PostAsync(string systemPrompt, object userObj, CancellationToken ct)
        {
            if (AppCfg.Offline || string.IsNullOrWhiteSpace(AppCfg.OpenRouterApiKey))
                return JsonDocument.Parse("{\"roleplay\":\"(offline stub)\"}").RootElement;

            var model = AppCfg.OpenRouterModel;  // your one chosen model
            return await PostWithModelAsync(model, systemPrompt, userObj, ct);
        }

        // With Fallback
        //private async Task<JsonElement> PostAsync(string systemPrompt, object userObj, CancellationToken ct)
        //{
        //    if (AppCfg.Offline || string.IsNullOrWhiteSpace(AppCfg.OpenRouterApiKey))
        //        return JsonDocument.Parse("{\"roleplay\":\"(offline stub)\"}").RootElement;

        //    var primary = AppCfg.OpenRouterModel;
        //    var sequence = new[] { primary }
        //        .Concat(FallbackModels)
        //        .Where(m => !string.Equals(m, primary, StringComparison.OrdinalIgnoreCase))
        //        .ToArray();

        //    Exception? last = null;

        //    foreach (var model in sequence)
        //    {
        //        // up to 2 quick retries per model for transient upstream hiccups
        //        for (int attempt = 1; attempt <= 2; attempt++)
        //        {
        //            try
        //            {
        //                if (attempt > 1) await Task.Delay(300 * attempt, ct); // tiny backoff
        //                return await PostWithModelAsync(model, systemPrompt, userObj, ct);
        //            }
        //            catch (HttpRequestException ex) when (IsModelUnavailable(ex) || IsTransient(ex))
        //            {
        //                last = ex;
        //                var why = IsModelUnavailable(ex) ? "unavailable" : "busy";
        //                Console.WriteLine($"[OpenRouter] {model} {why} (attempt {attempt}).");
        //                // If it's unavailable (404), break to next model. If transient, retry once.
        //                if (IsModelUnavailable(ex)) break;
        //            }
        //        }
        //        Console.WriteLine($"[OpenRouter] switching from {model} → next fallback…");
        //    }

        //    // If everything failed, surface the last error, but still return safe JSON for the game loop.
        //    Console.WriteLine($"[OpenRouter] All fallbacks exhausted. {last?.Message}");
        //    return JsonDocument.Parse("{\"roleplay\":\"(AI error — see console)\"}").RootElement;
        //}

        private static bool IsModelUnavailable(HttpRequestException ex)
        {
            // Your throw message looks like: "OpenRouter 404 Not Found. Body: { ... }"
            return ex.Message.Contains(" 404 ")
                || ex.Message.Contains("No endpoints found", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTransient(HttpRequestException ex)
        {
            // Treat provider rate limits / upstream hiccups as transient
            return ex.Message.Contains(" 429 ")
                || ex.Message.Contains(" 500 ")
                || ex.Message.Contains(" 502 ")
                || ex.Message.Contains(" 503 ")
                || ex.Message.Contains(" 504 ")
                || ex.Message.Contains(" 524 ") // “Provider returned error”
                || ex.Message.Contains("rate-limited upstream", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("temporarily rate-limited", StringComparison.OrdinalIgnoreCase);
        }

        private static JsonElement ParseLoose(string raw)
        {
            try { return JsonDocument.Parse(raw).RootElement.Clone(); }
            catch
            {
                int s = raw.IndexOf('{'); int e = raw.LastIndexOf('}');
                if (s >= 0 && e > s)
                    return JsonDocument.Parse(raw.Substring(s, e - s + 1)).RootElement.Clone();
                return JsonDocument.Parse("{}").RootElement.Clone();
            }
        }

        private static IEnumerable<object> BuildMessages(IEnumerable<ChatMessage> history, string systemPrompt, object userObj, int maxMessages)
        {
            yield return new { role = "system", content = systemPrompt };
            foreach (var m in System.Linq.Enumerable.TakeLast(history, maxMessages))
                yield return new { role = m.role, content = m.content };
            yield return new { role = "user", content = JsonSerializer.Serialize(userObj) };
        }

        private async Task<JsonElement> PostWithModelAsync(string model, string systemPrompt, object userObj, CancellationToken ct)
        {
            const string url = "https://openrouter.ai/api/v1/chat/completions";
            var messages = BuildMessages(_history, systemPrompt, userObj, AppCfg.HistoryMaxMessages);

            var payload = new
            {
                model,
                temperature = 0.6,
                response_format = new { type = "json_object" },
                max_tokens = AppCfg.MaxTokens,
                messages
            };
            for (int attempt = 1; attempt <= AppCfg.OpenRouterRetries + 1; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Version = System.Net.HttpVersion.Version20,
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {AppCfg.OpenRouterApiKey}");
                req.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost");
                req.Headers.TryAddWithoutValidation("X-Title", "ClocktowerDemo");

                HttpResponseMessage resp;
                string body;

                try
                {
                    resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    body = await resp.Content.ReadAsStringAsync(ct);
                }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt <= AppCfg.OpenRouterRetries)
                {
                    await Task.Delay(AppCfg.OpenRouterBackoffMs * attempt, ct);
                    continue; // retry same model
                }
                catch (HttpRequestException) when (attempt <= AppCfg.OpenRouterRetries)
                {
                    await Task.Delay(AppCfg.OpenRouterBackoffMs * attempt, ct);
                    continue; // retry same model
                }

                if (!resp.IsSuccessStatusCode)
                {
                    // Optional: retry SAME model on transient errors
                    var status = (int)resp.StatusCode;
                    var transient = status == 429 || status == 524 || status >= 500;
                    if (transient && attempt <= AppCfg.OpenRouterRetries)
                    {
                        await Task.Delay(AppCfg.OpenRouterBackoffMs * attempt, ct);
                        continue;
                    }

                    throw new HttpRequestException($"OpenRouter {status} {resp.ReasonPhrase}. Body: {body}");
                }

                var text = ExtractAssistantText(body);
                if (string.IsNullOrWhiteSpace(text))
                    return JsonDocument.Parse("{\"roleplay\":\"\"}").RootElement;

                // Try JSON first, then wrap plain text
                try { return JsonDocument.Parse(text!).RootElement; }
                catch
                {
                    var wrapped = JsonSerializer.Serialize(new { roleplay = text });
                    return JsonDocument.Parse(wrapped).RootElement;
                }
            }

            // All retries failed
            return JsonDocument.Parse("{\"roleplay\":\"(the archivist doesn’t answer—please try again)\"}").RootElement;
        }
        
        // With Fallback
        //private async Task<JsonElement> PostWithModelAsync(string model, string systemPrompt, object userObj, CancellationToken ct)
        //{
        //    const string url = "https://openrouter.ai/api/v1/chat/completions";

            //    var models = new List<string>();
            //    if (!string.IsNullOrWhiteSpace(model)) models.Add(model);
            //    models.AddRange(FallbackModels);

            //    // Trim history and build payload once per attempt
            //    var messages = BuildMessages(_history, systemPrompt, userObj, AppCfg.HistoryMaxMessages);

            //    foreach (var m in System.Linq.Enumerable.Distinct(models, StringComparer.OrdinalIgnoreCase))
            //    {
            //        var payload = new
            //        {
            //            model = m,
            //            temperature = 0.6,
            //            response_format = new { type = "json_object" },
            //            max_tokens = AppCfg.MaxTokens,
            //            messages
            //        };

            //        for (int attempt = 1; attempt <= AppCfg.OpenRouterRetries + 1; attempt++)
            //        {
            //            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            //            {
            //                Version = System.Net.HttpVersion.Version20,
            //                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            //            };
            //            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {AppCfg.OpenRouterApiKey}");
            //            req.Headers.TryAddWithoutValidation("HTTP-Referer", "http://localhost");
            //            req.Headers.TryAddWithoutValidation("X-Title", "ClocktowerDemo");

            //            HttpResponseMessage resp;
            //            string body;

            //            try
            //            {
            //                resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            //                body = await resp.Content.ReadAsStringAsync(ct);
            //            }
            //            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt <= AppCfg.OpenRouterRetries)
            //            {
            //                await Task.Delay(AppCfg.OpenRouterBackoffMs * attempt, ct);
            //                continue;
            //            }
            //            catch (HttpRequestException) when (attempt <= AppCfg.OpenRouterRetries)
            //            {
            //                await Task.Delay(AppCfg.OpenRouterBackoffMs * attempt, ct);
            //                continue;
            //            }

            //            if (!resp.IsSuccessStatusCode)
            //            {
            //                // Parse OpenRouter error (if present) to decide whether to skip to NEXT MODEL
            //                string? errMsg = null; int errCode = 0;
            //                try
            //                {
            //                    using var doc = JsonDocument.Parse(body);
            //                    if (doc.RootElement.TryGetProperty("error", out var e))
            //                    {
            //                        errMsg = e.TryGetProperty("message", out var mmsg) ? mmsg.GetString() : null;
            //                        errCode = e.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
            //                    }
            //                }
            //                catch { /* body not JSON */ }

            //                // Conditions to SKIP this model entirely:
            //                var status = (int)resp.StatusCode;
            //                bool invalidSlug = status == 400 && (errMsg?.IndexOf("not a valid model id", StringComparison.OrdinalIgnoreCase) >= 0);
            //                bool noEndpoints = status == 404 || (errMsg?.IndexOf("No endpoints found", StringComparison.OrdinalIgnoreCase) >= 0);
            //                bool rateLimited = status == 429;
            //                bool provider524 = errCode == 524;

            //                if (invalidSlug || noEndpoints || rateLimited)
            //                {
            //                    Console.WriteLine($"[OpenRouter] {m} unavailable (attempt {attempt}).");
            //                    Console.WriteLine("[OpenRouter] switching from {0} \u241B next fallback.", m);
            //                    break; // go to next model in the outer foreach
            //                }

            //                // Retry transient 5xx/524 on SAME model
            //                if (status >= 500 || provider524)
            //                {
            //                    if (attempt <= AppCfg.OpenRouterRetries)
            //                    {
            //                        await Task.Delay(AppCfg.OpenRouterBackoffMs * attempt, ct);
            //                        continue;
            //                    }
            //                }

            //                // Non-transient: throw so the engine catch shows a friendly line
            //                throw new HttpRequestException($"OpenRouter {status} {resp.ReasonPhrase}. Body: {body}");
            //            }

            //            // Success: normalize content to JSON
            //            var text = ExtractAssistantText(body);
            //            return ParseLoose(string.IsNullOrWhiteSpace(text) ? "{\"roleplay\":\"\"}" : text!);
            //        }
            //    }

            //    // All models exhausted
            //    return JsonDocument.Parse("{\"roleplay\":\"(the archivist doesn’t answer—please try again)\"}").RootElement;
            //}



        private static string ExtractAssistantText(string body)
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // If it's an error object, surface it clearly
            if (root.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : "(no message)";
                var code = err.TryGetProperty("code", out var c) ? c.ToString() : "(no code)";
                throw new HttpRequestException($"OpenRouter error {code}: {msg}");
            }

            // Normal OpenRouter shape: choices[0].message.content (string)
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var ch = choices[0];

                // message.content could be a string...
                if (ch.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var cont))
                {
                    if (cont.ValueKind == JsonValueKind.String)
                        return cont.GetString() ?? "";

                    // ...or an array of segments (some providers)
                    if (cont.ValueKind == JsonValueKind.Array)
                    {
                        var sb = new System.Text.StringBuilder();
                        foreach (var seg in cont.EnumerateArray())
                        {
                            // common pattern: { "type": "output_text", "text": "..." }
                            if (seg.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                                sb.Append(t.GetString());
                        }
                        return sb.ToString();
                    }
                }

                // Some providers place text in delta.content (non-streaming single chunk)
                if (ch.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var dcont) &&
                    dcont.ValueKind == JsonValueKind.String)
                {
                    return dcont.GetString() ?? "";
                }
            }

            // Couldn’t recognize; return the whole body for visibility
            return body;
        }
    }
}
