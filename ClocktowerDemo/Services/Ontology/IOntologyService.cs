using System.Collections.Generic;

namespace ClocktowerDemo.Services.Ontology
{
    public interface IOntologyService
    {
        // ---- NPC / emotion / hints ----
        (string? Hint, string? Item) GetHintForNpc(string npcName, int trust);
        List<string> GetEmotionalRange(string npcName);
        string? GetCurrentEmotion(string npcName);
        string DeriveEmotionForTrust(string npcName, int trust);

        // Optional NPC descriptors (use only if you want them in facts.npc_context)
        string? GetNpcCoreAttitude(string npcName);
        bool? GetNpcCanInitiate(string npcName);

        // ---- Items ----
        string? GetItemUseByLocalName(string localName);       // e.g., emblem -> "Unlock the Steam Catacombs..."
        string? GetItemFoundAtLocalName(string localName);     // e.g., emblem -> "library"
        List<string> FindItemsByTokens(List<string> tokens);   // token -> local names (emblem, oil_can, ...)

        // Extra item relationships
        List<string> GetClueTextsForItem(string itemLocal);    // all clue texts that are about this item
        List<(string Puzzle, string Trigger, string Location)> GetPuzzlesTriggeredByItem(string itemLocal);

        // ---- Puzzles ----
        string? GetPuzzleTriggerByLocalName(string localName);   // e.g., alleyGatePuzzle -> "Emblem used on gate"
        string? GetPuzzleLocationByLocalName(string localName);  // e.g., alleyGatePuzzle -> "alley"
        List<string> FindPuzzlesByTokens(List<string> tokens);

        // ---- Locations ----
        string? GetLocationKeyProp(string locationLocal);        // e.g., library -> "emblem"

        // ---- Utility ----
        List<(string Subject, string Label)> SearchByLabel(string keyword, int maxHits = 3);
    }
}

