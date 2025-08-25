using ClocktowerDemo.Configuration;
using ClocktowerDemo.Domain;
using ClocktowerDemo.Services.AI;
using ClocktowerDemo.Services.Ontology;
using ClocktowerDemo.Services.Politeness;
using ClocktowerDemo.Services.Trust;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ClocktowerDemo.Application
{
    public class GameEngine : IDisposable
    {
        private readonly IOntologyService _onto;
        private readonly IAIProvider _ai;
        private readonly IPolitenessDetector _polite;
        private readonly GameState _state;

        // cache pretty JSON options once (perf)
        private static readonly JsonSerializerOptions s_pretty = new() { WriteIndented = true };

        public GameEngine(IOntologyService onto, IAIProvider ai, IPolitenessDetector polite, GameState state)
        { _onto = onto; _ai = ai; _polite = polite; _state = state; }

        public async Task RunAsync(CancellationToken ct = default)
        {
            Console.WriteLine("=== Clocktower Demo ===");
            Console.WriteLine("Commands: /state /reset /exit");
            Console.WriteLine($"AI_MODE={AppCfg.AiMode}, POLITENESS_MODE={AppCfg.PolitenessMode}\n");

            while (!ct.IsCancellationRequested)
            {
                Console.Write("\nYou> ");
                var line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (line.Length == 0) continue;

                // char overload (analyzer suggestion)
                if (line.StartsWith('/'))
                {
                    var cmd = line.ToLowerInvariant();
                    if (cmd == "/exit") break;
                    if (cmd == "/reset") { Reset(); continue; }
                    if (cmd == "/state") { DumpState(); continue; }
                    Console.WriteLine("Unknown command."); continue;
                }

                await HandleTurn(line, ct);
            }
        }

        private async Task HandleTurn(string player, CancellationToken ct)
        {
            var npc = _state.CurrentNpc;
            var loc = _state.Location;

            // 1) Politeness → trust → emotion (keep what you already have)
            var sw = Stopwatch.StartNew();
            var pol = await _polite.ClassifyAsync(player, ct);
            sw.Stop();
            TrustService.UpdateTrust(_state, npc, pol.Label);
            var emotion = _onto.DeriveEmotionForTrust(npc, _state.Trust[npc].Trust);
            DebugBlock("POLITENESS", new { pol.Label, Confidence = Math.Round(pol.Confidence, 3) }, sw.ElapsedMilliseconds);
            DebugBlock("TRUST/EMOTION", new { npc, trust = _state.Trust[npc].Trust, emotion }, 0);

            // keep chat history line
            _state.ChatHistory.Add(new ChatMessage("user", $"[{loc}/{npc} tone={pol.Label}] {player}"));

            // 2) Gather facts from ontology based on the player's text
            sw.Restart();
            var (facts, factsMissing) = BuildFactsFromInput(player, npc);
            sw.Stop();
            if (facts.Count > 0) DebugBlock("FACTS RESOLVED", facts, sw.ElapsedMilliseconds);
            if (factsMissing.Count > 0) DebugBlock("FACTS MISSING", factsMissing, 0);

            // 3) Single LLM call
            JsonElement role;
            try
            {
                role = await _ai.RoleplayAsync(new
                {
                    npc,
                    location = loc,
                    npc_emotion = emotion,
                    player_utterance = player,
                    facts,
                    facts_missing = factsMissing
                }, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n(AI error) {ex.Message}");
                Console.WriteLine($"\n{npc}> (the archivist doesn’t answer—please try again)");
                return;
            }

            // Fallback
            //JsonElement role;
            //sw.Restart();
            //try
            //{
            //    role = await _ai.RoleplayAsync(new
            //    {
            //        npc,
            //        location = loc,
            //        npc_emotion = emotion,
            //        player_utterance = player,
            //        facts,
            //        facts_missing = factsMissing
            //    }, ct);
            //}
            //catch (Exception ex)
            //{
            //    sw.Stop();
            //    Console.WriteLine($"\n(AI error) {ex.Message}");
            //    Console.WriteLine($"\n{npc}> (the archivist doesn’t answer—please try again)");
            //    _state.ChatHistory.Add(new ChatMessage("assistant", "(the archivist doesn’t answer—please try again)"));
            //    return;
            //}
            sw.Stop();
            DebugBlock("AI ROLEPLAY", role, sw.ElapsedMilliseconds);

            // 4) Print
            var finalText = role.TryGetProperty("roleplay", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString() ?? "(no reply)"
                : "(no reply)";

            Console.WriteLine($"\n{npc}> {finalText}");
            _state.ChatHistory.Add(new ChatMessage("assistant", finalText));
        }

        // --- helpers ---

        private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
        {
            "the","a","an","to","of","in","on","for","and","or","but","is","are","was","were","be","do","does",
            "about","please","tell","me","you","your","my","this","that","these","those","with","from","at","as","it","i"
        };

        private static List<string> ExtractTokens(string text)
        {
            var toks = new List<string>();
            foreach (Match m in Regex.Matches(text.ToLowerInvariant(), "[a-z0-9_]+"))
            {
                var w = m.Value;
                if (w.Length < 2) continue;
                if (!Stop.Contains(w)) toks.Add(w);
            }
            return toks;
        }

        private (Dictionary<string, object> facts, List<string> missing) BuildFactsFromInput(string playerText, string npc)
        {
            var tokens = ExtractTokens(playerText);
            var facts = new Dictionary<string, object>();
            var missing = new List<string>();

            // npc context
            var (hint, assocItem) = _onto.GetHintForNpc(npc, _state.Trust[npc].Trust);
            var npcContext = new Dictionary<string, object?>
            {
                ["emotion"] = _onto.DeriveEmotionForTrust(npc, _state.Trust[npc].Trust),
                ["trust"] = _state.Trust[npc].Trust,
                ["hint"] = hint,
                ["hint_item"] = assocItem,
                ["name"] = npc
            };
            facts["npc_context"] = npcContext;
            facts["location"] = _state.Location;

            // OPTIONAL: location’s key item (add right after location)
            var keyItem = _onto.GetLocationKeyProp(_state.Location);
            if (!string.IsNullOrWhiteSpace(keyItem))
                facts["location_key_item"] = keyItem;

            // --- Items block (you already have this) ---
            var itemNames = _onto.FindItemsByTokens(tokens);
            if (itemNames.Count == 0 && tokens.Contains("emblem")) itemNames.Add("emblem");

            if (itemNames.Count > 0)
            {
                var items = new Dictionary<string, object>();
                foreach (var name in itemNames)
                {
                    var use = _onto.GetItemUseByLocalName(name);
                    var where = _onto.GetItemFoundAtLocalName(name);
                    var obj = new Dictionary<string, object>();
                    if (!string.IsNullOrWhiteSpace(use)) obj["use"] = use;
                    if (!string.IsNullOrWhiteSpace(where)) obj["found_at"] = where;
                    if (obj.Count > 0) items[name] = obj;
                }
                if (items.Count > 0) facts["items"] = items;
                else missing.AddRange(itemNames);
            }

            // >>> INSERT THE EMBLEM ENRICHMENT HERE (merge-safe) <<<
            if (itemNames.Contains("emblem"))
            {
                // Add NPC clue texts about the emblem
                var clues = _onto.GetClueTextsForItem("emblem");
                if (clues.Count > 0)
                {
                    if (!facts.TryGetValue("items", out var o) || o is not Dictionary<string, object> items)
                        items = (Dictionary<string, object>)(facts["items"] = new Dictionary<string, object>());
                    if (items.TryGetValue("emblem", out var eo) && eo is Dictionary<string, object> eobj)
                        eobj["clues"] = clues;
                }
            }

            // --- Puzzles block (you already have this) ---
            var puzzleNames = _onto.FindPuzzlesByTokens(tokens);
            if (puzzleNames.Count > 0)
            {
                var puzzles = new Dictionary<string, object>();
                foreach (var p in puzzleNames)
                {
                    var trigger = _onto.GetPuzzleTriggerByLocalName(p);
                    var loc = _onto.GetPuzzleLocationByLocalName(p);
                    var obj = new Dictionary<string, object>();
                    if (!string.IsNullOrWhiteSpace(trigger)) obj["trigger"] = trigger;
                    if (!string.IsNullOrWhiteSpace(loc)) obj["location"] = loc;
                    if (obj.Count > 0) puzzles[p] = obj;
                }
                if (puzzles.Count > 0) facts["puzzles"] = puzzles;
                else missing.AddRange(puzzleNames);
            }

            // >>> ADD EMBLEM-RELATED PUZZLES (merge-safe) <<<
            if (itemNames.Contains("emblem"))
            {
                var rel = _onto.GetPuzzlesTriggeredByItem("emblem");
                if (rel.Count > 0)
                {
                    if (!facts.TryGetValue("puzzles", out var po) || po is not Dictionary<string, object> puzzles)
                        puzzles = (Dictionary<string, object>)(facts["puzzles"] = new Dictionary<string, object>());

                    foreach (var (puz, trig, locn) in rel)
                        puzzles[puz] = new { trigger = trig, location = locn };
                }
            }

            // … your “mark unknowns as missing” code …
            if (facts.Count <= 2 && tokens.Count > 0)
                foreach (var t in tokens) if (!Stop.Contains(t)) missing.Add(t);

            return (facts, missing);
        }

        private static List<string> KeysWhereTrue(Dictionary<string, bool> dict)
        { var list = new List<string>(); foreach (var kv in dict) if (kv.Value) list.Add(kv.Key); return list; }

        private static List<string> ReadStrings(JsonElement root, string key)
        {
            var list = new List<string>();
            if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array)
                foreach (var it in el.EnumerateArray()) if (it.ValueKind == JsonValueKind.String) list.Add(it.GetString()!);
            return list;
        }
        private static bool TryGetBool(JsonElement root, string key) => root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.True;
        private static string? TryGetString(JsonElement root, string key) => root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        private static void AddMissing(Dictionary<string, List<string>> map, string key, string value)
        { if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>(); list.Add(value); }

        private static void DebugBlock(string title, object obj, long ms)
        {
            Console.WriteLine($"\n======== {title} ========");
            if (ms > 0) Console.WriteLine($"(took {ms} ms)");
            Console.WriteLine(JsonSerializer.Serialize(obj, s_pretty)); // reuse cached options
        }

        private void Reset() => _state.ChatHistory.Clear();

        private void DumpState()
        {
            DebugBlock("STATE", new
            {
                _state.Location,
                _state.CurrentNpc,
                trust = _state.Trust,
                inventory = _state.Inventory,
                flags = _state.Flags,
                chat_history_len = _state.ChatHistory.Count
            }, 0);
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this); // analyzer hint
        }
    }
}
