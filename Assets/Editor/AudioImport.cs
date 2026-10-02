using UnityEditor;
using UnityEngine;

namespace RowVR.Editor
{
    /// <summary>
    /// Import settings for recorded sounds, set in code like every other project setting, so
    /// a clip dropped into Assets/Resources/Audio is imported the same way on any machine.
    ///
    /// The stroke sounds are short and must play on the frame the stroke is detected, so they
    /// are decompressed into memory at load and never decoded on the audio thread. ADPCM
    /// keeps them small (about 3.5:1) at no CPU cost that matters on the Quest.
    /// </summary>
    sealed class AudioImport : AssetPostprocessor
    {
        const string Folder = "Assets/Resources/Audio/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (AudioImporter)assetImporter;
            importer.forceToMono = true;
            importer.loadInBackground = false;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
        }
    }
}
