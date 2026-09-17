using System.IO;
using UnityEditor;
using UnityEngine;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// Auto-configures sprite import settings for any new texture dropped into
    /// Assets/Resources/Cards/, since a plain Default-type texture makes Resources.Load&lt;Sprite&gt;
    /// silently return null until someone hand-configures the .meta.
    ///
    /// Runs automatically on import. Sets the texture type to Sprite / Multiple, enables crunch
    /// compression, and generates a single full-rect sub-sprite named "&lt;fileName&gt;_0". Only
    /// touches textures under Assets/Resources/Cards/, and only stamps a texture on its first
    /// import (spriteImportMode still None) so manual re-slicing later isn't clobbered.
    /// </summary>
    public class CardArtworkTextureImportSettings : AssetPostprocessor
    {
        private const string CardsResourcesFolder = "Assets/Resources/Cards/";

        private void OnPreprocessTexture()
        {
            if (!IsCardArtwork()) return;

            var importer = (TextureImporter)assetImporter;
            if (importer.spriteImportMode != SpriteImportMode.None) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 100;
            importer.spritePivot = new Vector2(0.5f, 0.5f);
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            var platformSettings = importer.GetDefaultPlatformTextureSettings();
            platformSettings.crunchedCompression = true;
            importer.SetPlatformTextureSettings(platformSettings);
        }

        private void OnPostprocessTexture(Texture2D texture)
        {
            if (!IsCardArtwork()) return;

            var importer = (TextureImporter)assetImporter;
            if (importer.spriteImportMode != SpriteImportMode.Multiple) return;

#pragma warning disable 618 // spritesheet is obsolete but simplest way to stamp one sub-sprite
                            // without pulling in the 2D Sprite package's data-provider API.
            if (importer.spritesheet != null && importer.spritesheet.Length > 0) return;

            var fileName = Path.GetFileNameWithoutExtension(assetPath);
            importer.spritesheet = new[]
            {
                new SpriteMetaData
                {
                    name = fileName + "_0",
                    rect = new Rect(0, 0, texture.width, texture.height),
                    alignment = (int)SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                },
            };
#pragma warning restore 618
        }

        private bool IsCardArtwork()
        {
            return assetPath.Replace('\\', '/').StartsWith(CardsResourcesFolder);
        }
    }
}
