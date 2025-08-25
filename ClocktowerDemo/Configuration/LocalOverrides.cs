// File: Configuration/LocalOverrides.cs
namespace ClocktowerDemo.Configuration
{
    public static class LocalOverrides
    {
        public static bool Enabled = true;

        // --- Python bridges (your paths) ---
        public static string? PYTHON_EXE = @"C:\Users\User\Desktop\ClocktowerC#\ClocktowerDemo\venv\Scripts\python.exe";
        public static string? PY_POLITENESS_SERVER = @"C:\Users\User\Desktop\ClocktowerC#\ClocktowerDemo\PyScripts\politeness_server.py";
        public static string? PY_MISTRAL_SERVER = @"C:\Users\User\Desktop\ClocktowerC#\ClocktowerDemo\PyScripts\mistral_server.py";

        // --- Ontology ---
        public static string? ONTOLOGY_FILE = @"C:\Users\User\Desktop\ClocktowerC#\ClocktowerDemo\Ontology\ClocktowerOntoTurt.ttl";

        // --- AI selection ---
        public static string? AI_MODE = "openrouter"; //"mistral_local"

        // --- Politeness selection ---
        public static string? POLITENESS_MODE = "http";
        public static string? POLITENESS_URL = "http://localhost:8001";

        // --- OpenRouter (optional for local Mistral) ---
        public static string? OPENROUTER_API_KEY = "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";
        public static string? OPENROUTER_MODEL = "deepseek/deepseek-r1-0528:free";

        // Offline stub (true = don’t call network)
        public static bool? OFFLINE = false;
    }
}
