using System.Linq;
using TCGCollector.Data;
using UnityEditor;
using UnityEngine;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// Scans the whole project for CardData assets and fills every CardDatabase asset found
    /// with the full list. Run this after adding/removing/renaming any card. With hundreds of
    /// cards per set this is much less error-prone than dragging assets into the list by hand.
    /// </summary>
    public static class CardDatabaseBuilder
    {
        [MenuItem("TCG Collector/Rebuild Card Database")]
        public static void Rebuild()
        {
            var databaseGuids = AssetDatabase.FindAssets("t:CardDatabase");
            if (databaseGuids.Length == 0)
            {
                Debug.LogError("[CardDatabaseBuilder] No CardDatabase asset found in the project. Create one via Assets > Create > TCG Collector > Card Database first.");
                return;
            }

            var cardGuids = AssetDatabase.FindAssets("t:CardData");
            var allCards = cardGuids
                .Select(guid => AssetDatabase.LoadAssetAtPath<CardData>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(card => card != null)
                .OrderBy(card => card.set != null ? card.set.name : string.Empty)
                .ThenBy(card => card.cardNumber)
                .ToList();

            var duplicateIds = allCards
                .Where(c => !string.IsNullOrEmpty(c.cardId))
                .GroupBy(c => c.cardId)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in duplicateIds)
            {
                Debug.LogWarning($"[CardDatabaseBuilder] cardId '{group.Key}' is used by {group.Count()} cards: " +
                                  string.Join(", ", group.Select(c => c.name)));
            }

            int missingIds = allCards.Count(c => string.IsNullOrEmpty(c.cardId));
            if (missingIds > 0)
                Debug.LogWarning($"[CardDatabaseBuilder] {missingIds} card(s) have no cardId set — they won't be saveable/trackable.");

            foreach (var dbGuid in databaseGuids)
            {
                var database = AssetDatabase.LoadAssetAtPath<CardDatabase>(AssetDatabase.GUIDToAssetPath(dbGuid));
                if (database == null) continue;

                database.allCards = allCards;
                database.InvalidateIndex();
                EditorUtility.SetDirty(database);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CardDatabaseBuilder] Rebuilt {databaseGuids.Length} CardDatabase asset(s) with {allCards.Count} card(s).");
        }
    }
}
