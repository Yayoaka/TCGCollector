using UnityEngine;

namespace TCGCollector.Data
{
    /// <summary>
    /// A License/franchise groups one or more CardSets under the same IP (e.g. "Riftbound", "Solo
    /// Leveling"). Purely organizational for the Profile screen — gameplay logic still operates
    /// at the CardSet level. Create instances via Assets > Create > TCG Collector > License.
    /// </summary>
    [CreateAssetMenu(fileName = "License_", menuName = "TCG Collector/License", order = 0)]
    public class License : ScriptableObject
    {
        [Tooltip("Unique stable id. Do not change once players own cards under this license.")]
        public string licenseId;

        public string displayName;

        [Tooltip("Cover art / logo shown on the Profile screen's license recap.")]
        public Sprite coverArt;

        [Tooltip("Used only to order licenses in menus (lower = shown first).")]
        public int sortOrder;

        public override string ToString() => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}
