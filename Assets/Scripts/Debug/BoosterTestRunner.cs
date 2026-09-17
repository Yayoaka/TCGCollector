using System.Text;
using TCGCollector.Core;
using TCGCollector.Data;
using TCGCollector.Save;
using UnityEngine;

namespace TCGCollector.DebugTools
{
    /// <summary>
    /// Temporary test harness for the booster/collection/save pipeline before any UI exists. Put
    /// on a GameObject alongside GameManager, assign a Set, then use the "Open Test Booster"
    /// context menu. Safe to delete once the real booster screen exists.
    /// </summary>
    public class BoosterTestRunner : MonoBehaviour
    {
        [SerializeField] private CardSet setToTest;

        [ContextMenu("Open Test Booster")]
        public void OpenTestBooster()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogError("[BoosterTestRunner] No GameManager in the scene.");
                return;
            }

            if (setToTest == null)
            {
                Debug.LogError("[BoosterTestRunner] Assign a CardSet in the inspector first.");
                return;
            }

            var results = GameManager.Instance.OpenBooster(setToTest);

            if (results.Count == 0)
            {
                Debug.LogWarning("[BoosterTestRunner] Booster opened but returned 0 cards — check that the Set has cards with a Rarity assigned, and that the Card Database has been rebuilt (TCG Collector > Rebuild Card Database).");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Opened booster from '{setToTest.displayName}' — {results.Count} cards:");
            foreach (var pull in results)
            {
                string tag = pull.WasNew ? "NOUVEAU" : "Doublon";
                sb.AppendLine($"  [{tag}] {pull.Card.displayName} ({pull.Card.rarity?.displayName ?? "?"})");
            }

            var (owned, total) = GameManager.Instance.Collection.GetSetCompletionCounts(setToTest);
            sb.AppendLine($"Complétion du set : {owned}/{total} ({GameManager.Instance.Collection.GetSetCompletionRatio(setToTest):P0})");

            Debug.Log(sb.ToString());
        }

        [ContextMenu("Wipe Save File")]
        public void WipeSave()
        {
            SaveManager.DeleteSave();
            Debug.Log("[BoosterTestRunner] Save file deleted. Restart Play mode to start fresh.");
        }
    }
}
