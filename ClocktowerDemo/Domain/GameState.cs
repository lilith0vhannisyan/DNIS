using System.Collections.Generic;

namespace ClocktowerDemo.Domain
{
    public class GameState
    {
        public string Location { get; set; } = "library";
        public string CurrentNpc { get; set; } = "Iris";

        public Dictionary<string, bool> Inventory { get; set; } = new()
        {
            ["oil_can"] = false,
            ["emblem"] = false,
            ["scrap_metal"] = false,
            ["winding_key"] = false,
            ["gear_left"] = false,
            ["gear_right"] = false
        };

        public Dictionary<string, bool> Flags { get; set; } = new()
        {
            ["booksSolved"] = false,
            ["gateUnlocked"] = false,
            ["cratesSolved"] = false,
            ["forgeDone"] = false,
            ["barrier_Iris"] = false,
            ["barrier_Piper"] = false,
            ["barrier_Garrick"] = false
        };

        public Dictionary<string, NPCState> Trust { get; set; } = new()
        {
            ["Iris"] = new NPCState(),
            ["Piper"] = new NPCState(),
            ["Garrick"] = new NPCState()
        };

        public List<ChatMessage> ChatHistory { get; set; } = new();
    }
}
