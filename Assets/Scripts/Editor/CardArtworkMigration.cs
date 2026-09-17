using System.Collections.Generic;
using System.IO;
using TCGCollector.Data;
using UnityEditor;
using UnityEngine;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// One-time (safe to re-run) migration: for every CardData still using the old direct
    /// "artwork" Sprite field (see CardData.artwork - deprecated), copies the image into
    /// Resources/Cards/&lt;cardId&gt;.&lt;ext&gt; (the path CardArtworkLoader expects), clears the
    /// old field, and deletes the now-unused original.
    ///
    /// This matters because a Sprite field on CardData stays loaded for the whole session and
    /// never releases, which is the likely cause of crashes after opening many packs. New cards
    /// don't need this - just drop the image straight into Resources/Cards.
    ///
    /// Run TCG Collector &gt; Migrate Artwork To Resources once. Safe to re-run - already-migrated
    /// or art-less cards are skipped, and existing destinations aren't re-copied.
    /// </summary>
    public static class CardArtworkMigration
    {
        private const string ResourcesFolder = "Assets/Resources";
        private const string CardsSubfolder = "Cards";

        [MenuItem("TCG Collector/Migrate Artwork To Resources")]
        public static void Migrate()
        {
            EnsureResourcesCardsFolderExists();

            int migrated = 0, skippedNoArt = 0, skippedNoCardId = 0, errors = 0;

            // Deletions are deferred until every card is processed - two cards can share the same
            // source image (e.g. a reprint), and deleting it early would break the other's pending copy.
            var sourcePathsToDelete = new HashSet<string>();

            foreach (var cardGuid in AssetDatabase.FindAssets("t:CardData"))
            {
                var cardPath = AssetDatabase.GUIDToAssetPath(cardGuid);
                var card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);
                if (card == null) continue;

                if (card.artwork == null) { skippedNoArt++; continue; }

                if (string.IsNullOrEmpty(card.cardId))
                {
                    Debug.LogWarning($"[CardArtworkMigration] '{cardPath}' has artwork but no cardId set - skipping (can't compute its Resources path).");
                    skippedNoCardId++;
                    continue;
                }

                var sourcePath = AssetDatabase.GetAssetPath(card.artwork);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    Debug.LogWarning($"[CardArtworkMigration] '{cardPath}': couldn't resolve a source file for its artwork - leaving it untouched.");
                    errors++;
                    continue;
                }

                var ext = Path.GetExtension(sourcePath);
                var destPath = $"{ResourcesFolder}/{CardsSubfolder}/{card.cardId}{ext}";

                if (sourcePath == destPath)
                {
                    // Already at the expected path - just clear the field reference.
                    card.artwork = null;
                    EditorUtility.SetDirty(card);
                    migrated++;
                    continue;
                }

                if (AssetDatabase.LoadAssetAtPath<Sprite>(destPath) == null)
                {
                    var copyOk = AssetDatabase.CopyAsset(sourcePath, destPath);
                    if (!copyOk)
                    {
                        Debug.LogWarning($"[CardArtworkMigration] '{cardPath}': failed to copy '{sourcePath}' to '{destPath}' - leaving this card's old reference in place for now.");
                        errors++;
                        continue;
                    }
                }

                sourcePathsToDelete.Add(sourcePath);
                card.artwork = null;
                EditorUtility.SetDirty(card);
                migrated++;
            }

            foreach (var sourcePath in sourcePathsToDelete)
                AssetDatabase.DeleteAsset(sourcePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[CardArtworkMigration] Migrated {migrated} card(s) to Resources/Cards ({sourcePathsToDelete.Count} original file(s) removed). " +
                      $"{skippedNoArt} card(s) already had no artwork field set, {skippedNoCardId} skipped for missing cardId, {errors} error(s) - see warnings above if that's non-zero.");
        }

        private static void EnsureResourcesCardsFolderExists()
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder))
                AssetDatabase.CreateFolder("Assets", "Resources");

            if (!AssetDatabase.IsValidFolder($"{ResourcesFolder}/{CardsSubfolder}"))
                AssetDatabase.CreateFolder(ResourcesFolder, CardsSubfolder);
        }
    }
}
