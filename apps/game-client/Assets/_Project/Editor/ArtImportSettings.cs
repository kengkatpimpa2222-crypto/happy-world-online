using UnityEditor;
using UnityEngine;

namespace HappyWorld.EditorTools
{
    /// <summary>
    /// Import settings for the bundled art (Resources/Art), applied automatically so nobody has to click
    /// through the inspector: crisp uncompressed colormaps and sprites without mipmaps, and on the audio side long
    /// music/ambience streamed from disk (small RAM on cheap phones) while short effects load decompressed
    /// so they play without delay.
    /// </summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        private const string ArtRoot = "/Resources/Art/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains(ArtRoot)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.mipmapEnabled = false;
            // model colormaps are flat colour cells (crisp); mini-game sprites are drawn art (smooth)
            t.filterMode = assetPath.Contains("/Art/Models/") ? FilterMode.Point : FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            t.npotScale = TextureImporterNPOTScale.None;
            t.textureCompression = TextureImporterCompression.Uncompressed; // 512x512 palette: compression smears the colour cells
            t.isReadable = false;
        }

        private void OnPreprocessAudio()
        {
            if (!assetPath.Contains(ArtRoot)) return;
            var a = (AudioImporter)assetImporter;
            var file = System.IO.Path.GetFileName(assetPath);
            bool longClip = file.StartsWith("music_") || file.StartsWith("amb_");
            var s = a.defaultSampleSettings;
            s.loadType = longClip ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = longClip ? 0.45f : 0.7f;
            a.defaultSampleSettings = s;
            a.forceToMono = !file.StartsWith("music_");
            a.loadInBackground = longClip;
        }
    }
}
