using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>
    /// On/off preference for the short vibration played when a top-rarity card is revealed while
    /// opening a booster (see BoosterOpenScreen.ShowSequentialCard).
    ///
    /// Backed by PlayerPrefs rather than PlayerSaveData/SaveManager on purpose: this is a
    /// device/app-level preference, not collection data, so it survives the Profile screen's
    /// "Reset" button.
    /// </summary>
    public static class VibrationSettings
    {
        private const string PrefKey = "VibrationOnBestPullEnabled";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
