using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClocktowerDemo.Configuration;

namespace ClocktowerDemo.Services.AI
{
    // Talks to mistral_server.py (persistent). We send { "messages":[{role,content}...] } and expect {"content":"..."} back.
    public class PythonMistralProvider : IAIProvider, IDisposable
    {
        private readonly object _lock = new();
        private readonly Process _proc;

        public PythonMistralProvider()
        {
            var psi = new ProcessStartInfo
            {
                FileName = AppCfg.PythonExe,
                Arguments = $"\"{AppCfg.PyMistralServer}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            _proc = Process.Start(psi) ?? throw new Exception("Failed to start Python mistral server.");
        }

        public Task<JsonElement> PlannerAsync(object payload, CancellationToken ct)
            => SendAsync("You are an AI planner for a story-driven game. Output pure JSON: {\"need_additional_info\":true|false,\"needs\":[\"keys\"],\"draft_roleplay\":\"...\"}", payload, ct);

        public Task<JsonElement> RoleplayAsync(object payload, CancellationToken ct)
            => SendAsync("You are the NPC actor in a story-driven game. Stay in character, concise, only use provided facts. Output pure JSON: {\"need_additional_info\":false,\"roleplay\":\"...\"}.", payload, ct);

        private Task<JsonElement> SendAsync(string system, object userObj, CancellationToken ct)
        {
            var wrapper = new
            {
                messages = new object[]
                {
                    new { role = "system", content = system },
                    new { role = "user",   content = JsonSerializer.Serialize(userObj) }
                },
                max_new_tokens = 256,
                temperature = 0.6
            };
            var req = JsonSerializer.Serialize(wrapper);

            string line;
            lock (_lock)
            {
                _proc.StandardInput.WriteLine(req);
                _proc.StandardInput.Flush();
                line = _proc.StandardOutput.ReadLine();
            }
            if (string.IsNullOrWhiteSpace(line)) line = "{}";
            try
            {
                var doc = JsonDocument.Parse(line);
                var content = doc.RootElement.GetProperty("content").GetString() ?? "{}";
                return Task.FromResult(ParseLoose(content));
            }
            catch (Exception ex)
            {
                var err = _proc.StandardError.ReadToEnd();
                throw new Exception($"Mistral parse error. raw='{line}' stderr='{err}'", ex);
            }
        }

        private static JsonElement ParseLoose(string raw)
        {
            try { return JsonDocument.Parse(raw).RootElement.Clone(); }
            catch
            {
                int s = raw.IndexOf('{'); int e = raw.LastIndexOf('}');
                if (s >= 0 && e > s) return JsonDocument.Parse(raw.Substring(s, e - s + 1)).RootElement.Clone();
                return JsonDocument.Parse("{}").RootElement.Clone();
            }
        }

        public void Dispose()
        {
            try { _proc.StandardInput.WriteLine("{\"cmd\":\"shutdown\"}"); } catch { }
            try { if (!_proc.HasExited) _proc.Kill(true); } catch { }
            _proc.Dispose();
        }
    }
}
