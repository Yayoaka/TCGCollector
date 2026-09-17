using System.Collections.Generic;
using TCGCollector.Data;
using UnityEditor;
using UnityEngine;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// Card artwork is no longer assigned by hand (see CardData.artwork - deprecated); dropping
    /// the image into Assets/Resources/Cards/&lt;cardId&gt;.&lt;ext&gt; is enough for
    /// CardArtworkLoader to find it at runtime.
    ///
    /// This tool just checks that convention: run TCG Collector &gt; Validate Card Artwork to see
    /// which cards are missing their file in Resources/Cards.
    /// </summary>
    public static class CardArtworkAssigner
    {
        [MenuItem("TCG Collector/Validate Card Artwork")]
        public static void Validate()
        {
            int found = 0;
            var missing = new List<string>();

            foreach (var cardGuid in AssetDatabase.FindAssets("t:CardData"))
            {
                var cardPath = AssetDatabase.GUIDToAssetPath(cardGuid);
                var card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);
                if (card == null || string.IsNullOrEmpty(card.cardId)) continue;

                if (Resources.Load<Sprite>("Cards/" + card.cardId) != null)
                    found++;
                else
                    missing.Add(card.cardId);
            }

            var report = $"[CardArtworkAssigner] {found} card(s) have artwork in Resources/Cards.";
            if (missing.Count > 0)
                report += $" {missing.Count} card(s) still missing artwork: {string.Join(", ", missing)}";
            Debug.Log(report);
        }
    }
}
