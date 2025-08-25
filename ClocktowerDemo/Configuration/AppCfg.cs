using System;
using System.IO;

namespace ClocktowerDemo.Configuration
{
    public static class AppCfg
    {
        // AI request parametrs
        public static int MaxTokens = 256;               
        public static int OpenRouterRetries = 2;         // how many times to retry
        public static int OpenRouterBackoffMs = 500;     // backoff between retries
        public static int HistoryMaxMessages = 6;        // cap chat history sent each call
        public static TimeSpan HttpTimeout = TimeSpan.FromSeconds(60);

        // Defaults / env
        public static string OpenRouterApiKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "";
        // Don't forget change here model name Lilit!!!
        public static string OpenRouterModel = Environment.GetEnvironmentVariable("OPENROUTER_MODEL") ?? "deepseek/deepseek-r1-0528:free";
        public static string OntologyPath = Environment.GetEnvironmentVariable("ONTOLOGY_FILE") ?? "ClocktowerOntoTurt.ttl";

        public static string AiMode = (Environment.GetEnvironmentVariable("AI_MODE") ?? "openrouter").ToLower();  // openrouter
        public static string PolitenessMode = (Environment.GetEnvironmentVariable("POLITENESS_MODE") ?? "http").ToLower();

        public static string PythonExe = Environment.GetEnvironmentVariable("PYTHON_EXE") ?? "python";
        public static string PyPoliteServer = Environment.GetEnvironmentVariable("PY_POLITENESS_SERVER") ?? "politeness_server.py";
        public static string PyMistralServer = Environment.GetEnvironmentVariable("PY_MISTRAL_SERVER") ?? "mistral_server.py";

        public static string PolitenessUrl = Environment.GetEnvironmentVariable("POLITENESS_URL") ?? "http://localhost:8001";
        public static bool Offline = (Environment.GetEnvironmentVariable("OFFLINE") ?? "0") == "1";

        public static int OpenRouterMaxTokens = int.TryParse(Environment.GetEnvironmentVariable("OPENROUTER_MAXTOKENS"), out var v) ? v : 256;

        // Resolve helper for relative paths (relative to bin\<Debug|Release>\netX.Y\)
        static string Resolve(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return p;
            return Path.IsPathRooted(p)
                ? p
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
        }

        static AppCfg()
        {
            // Apply LocalOverrides if enabled
            if (!LocalOverrides.Enabled) return;

            // Non-path overrides
            if (!string.IsNullOrWhiteSpace(LocalOverrides.OPENROUTER_API_KEY)) OpenRouterApiKey = LocalOverrides.OPENROUTER_API_KEY!;
            if (!string.IsNullOrWhiteSpace(LocalOverrides.OPENROUTER_MODEL)) OpenRouterModel = LocalOverrides.OPENROUTER_MODEL!;
            if (!string.IsNullOrWhiteSpace(LocalOverrides.AI_MODE)) AiMode = LocalOverrides.AI_MODE!.ToLower();
            if (!string.IsNullOrWhiteSpace(LocalOverrides.POLITENESS_MODE)) PolitenessMode = LocalOverrides.POLITENESS_MODE!.ToLower();
            if (LocalOverrides.OFFLINE.HasValue) Offline = LocalOverrides.OFFLINE.Value;
            if (!string.IsNullOrWhiteSpace(LocalOverrides.POLITENESS_URL))
                PolitenessUrl = LocalOverrides.POLITENESS_URL!;

            // Path-like overrides (use Resolve so ..\..\.. works from bin folder)
            if (!string.IsNullOrWhiteSpace(LocalOverrides.PYTHON_EXE)) PythonExe = Resolve(LocalOverrides.PYTHON_EXE!);
            if (!string.IsNullOrWhiteSpace(LocalOverrides.PY_POLITENESS_SERVER)) PyPoliteServer = Resolve(LocalOverrides.PY_POLITENESS_SERVER!);
            if (!string.IsNullOrWhiteSpace(LocalOverrides.PY_MISTRAL_SERVER)) PyMistralServer = Resolve(LocalOverrides.PY_MISTRAL_SERVER!);
            if (!string.IsNullOrWhiteSpace(LocalOverrides.ONTOLOGY_FILE)) OntologyPath = Resolve(LocalOverrides.ONTOLOGY_FILE!);
        }

        // Optional: call this at startup to verify
        public static void PrintResolved()
        {
            Console.WriteLine("Resolved paths / settings:");
            Console.WriteLine($"  PythonExe:        {PythonExe}");
            Console.WriteLine($"  PolitenessServer: {PyPoliteServer}");
            Console.WriteLine($"  MistralServer:    {PyMistralServer}");
            Console.WriteLine($"  OntologyPath:     {OntologyPath}");
            Console.WriteLine($"  AiMode:           {AiMode}");
            Console.WriteLine($"  PolitenessMode:   {PolitenessMode}");
            Console.WriteLine($"  Offline:          {Offline}");
            Console.WriteLine();
        }
    }
}