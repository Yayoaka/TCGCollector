using UnityEditor;
using UnityEngine;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// Enables Crunch compression on every card texture under Assets/Data to shrink build size
    /// without affecting resolution or runtime quality (it decompresses to the same GPU format
    /// once loaded).
    ///
    /// Run TCG Collector > Optimize Card Artwork (Crunch Compression) after adding new card art.
    /// Safe to re-run - already-crunched textures are skipped.
    /// </summary>
    public static class BulkOptimizeCardArtwork
    {
        // Matches the compressionQuality used by the project's existing card textures.
        private const int CrunchQuality = 50;

        [MenuItem("TCG Collector/Optimize Card Artwork (Crunch Compression)")]
        public static void OptimizeCardArtwork()
        {
            int changed = 0, alreadyDone = 0, skippedNotTexture = 0;

            // Scoped to Assets/Data so UI icons/fonts elsewhere are left untouched.
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Data" });

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
                {
                    skippedNotTexture++;
                    continue;
                }

                bool needsChange = !importer.crunchedCompression || importer.compressionQuality != CrunchQuality;

                if (!needsChange)
                {
                    alreadyDone++;
                    continue;
                }

                importer.crunchedCompression = true;
                importer.compressionQuality = CrunchQuality;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
                changed++;
            }

            Debug.Log($"[BulkOptimizeCardArtwork] Crunch-compressed {changed} texture(s), {alreadyDone} already optimized, {skippedNotTexture} non-texture asset(s) skipped. Check Build Settings > \"Build\" size (or File > Build Profiles) after a fresh build to see the size drop - this script only changes import settings, it doesn't shrink the currently-open Editor's cached data.");
        }
    }
}
