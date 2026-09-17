using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>
    /// On/off preference for "fast open": when enabled, the one-at-a-time booster reveal (see
    /// BoosterOpenScreen.ShowSequentialCard) only pauses on Rare-or-better/bonus slots, skipping
    /// the tap-by-tap pause on filler Common/Uncommon slots. The summary grid still shows every
    /// pulled card, and nothing is hidden from the collection or payout.
    ///
    /// Backed by PlayerPrefs rather than PlayerSaveData/SaveManager on purpose: this is a
    /// device/app-level preference, not collection data, so it survives the Profile screen's
    /// "Reset" button.
    /// </summary>
    public static class FastOpenSettings
    {
        private const string PrefKey = "FastOpenEnabled";

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}