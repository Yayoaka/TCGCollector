using UnityEngine;

namespace TCGCollector.Data
{
    /// <summary>
    /// Defines a rarity tier (Commune, Rare, Epique, Legendaire, ...).
    /// Create instances via Assets > Create > TCG Collector > Rarity.
    /// </summary>
    [CreateAssetMenu(fileName = "Rarity_", menuName = "TCG Collector/Rarity", order = 0)]
    public class CardRarity : ScriptableObject
    {
        [Tooltip("Unique stable id, used as a save-data key. Do not change once cards reference it in save files.")]
        public string rarityId;

        public string displayName;

        [Tooltip("Higher rank = rarer. Used for 'guaranteed minimum rarity' comparisons in boosters.")]
        public int rank;

        [Tooltip("Relative weight used when randomly drawing a card's rarity. Higher = more common.")]
        [Min(0f)]
        public float dropWeight = 1f;

        [Tooltip("Used to tint UI elements (borders, labels) for this rarity.")]
        public Color accentColor = Color.white;

        [Tooltip("Optional small icon shown next to the rarity name.")]
        public Sprite icon;

        [Tooltip("Prix de reference de cette rarete - sert de base au rachat systeme des doublons en trop (85% de cette valeur, voir CollectionManager.GetSystemBuybackPrice) dans l'onglet Rachat de la Boutique. Le petit bonus automatique donne a chaque doublon tire d'un booster ne depend plus de cette valeur, voir CollectionManager.AddCards (base sur le rang de la rarete a la place).")]
        [Min(0)]
        public int sellValue = 1;

        [Tooltip("Cochee : tirer une carte de cette rarete declenche une petite vibration a l'ouverture du booster (si les vibrations sont activees dans Profil > Parametres). A toi de decider par Set/Licence quelles raretes valent le coup - par exemple decoche une rarete qui sort a chaque booster (pas un vrai hit), ou coche plusieurs raretes differentes qui meritent toutes une vibration meme si elles n'ont pas le rang le plus eleve.")]
        public bool vibrateOnPull = false;

        [Tooltip("Cochee : cette rarete peut apparaitre dans la Boutique (achat direct d'une carte precise contre des po, en plus des boosters - voir ShopManager). A toi de decider par jeu/licence quelles raretes comptent comme le haut du panier a proposer (par exemple coche seulement Legendaire, ou Epique+Legendaire) - la boutique propose toujours en priorite la meilleure rarete cochee qui a encore au moins une carte non possedee.")]
        public bool shopEligible = false;

        [Tooltip("Cout en po pour acheter directement une carte de cette rarete dans la Boutique. Ignore si Shop Eligible est decochee.")]
        [Min(0)]
        public int shopPrice = 500;

        public override string ToString() => string.IsNullOrEmpty(displayName) ? name : displayName;
    }
}

