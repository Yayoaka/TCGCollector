using System;
using System.Threading.Tasks;
using TCGCollector.Data;
using TCGCollector.Save;
using TCGCollector.Systems;
using Unity.Services.Authentication;
using UnityEngine;

namespace TCGCollector.Core
{
    /// <summary>
    /// Scene bootstrap. Put one instance in your boot scene, assign CardDatabase and default
    /// BoosterConfig in the inspector - every other system reads through this singleton, which
    /// survives scene loads via DontDestroyOnLoad.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private BoosterConfig defaultBoosterConfig;

        [Header("Google Sign-In (optionnel)")]
        [Tooltip("Assigne un asset GoogleAuthConfig (Assets > Create > TCG Collector > Google " +
                 "Auth Config) avec tes propres identifiants. Laisser vide desactive le bouton " +
                 "'Se connecter avec Google' (l'app reste en auth anonyme). Ne jamais remettre ces " +
                 "identifiants en champs directs ici - un asset separe reste hors de git (voir " +
                 ".gitignore), un champ ici finirait serialise en clair dans la scene.")]
        [SerializeField] private GoogleAuthConfig googleAuthConfig;

        public CardDatabase CardDatabase => cardDatabase;
        public BoosterConfig DefaultBoosterConfig => defaultBoosterConfig;

        public CollectionManager Collection { get; private set; }
        public BoosterOpeningService BoosterService { get; private set; }
        public QuestManager Quests { get; private set; }
        public ShopManager Shop { get; private set; }
        public MarketplaceService Marketplace { get; private set; }
        public CloudSyncService CloudSync { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (cardDatabase == null)
                Debug.LogError("[GameManager] No CardDatabase assigned in the inspector.");

            // Loaded once here and shared with every system that reads/writes save data, so no
            // two systems can load independent copies and silently overwrite each other's changes.
            var saveData = SaveManager.Load();

            Collection = new CollectionManager(cardDatabase, saveData);
            BoosterService = new BoosterOpeningService();
            Quests = new QuestManager(saveData, Collection);
            Shop = new ShopManager(cardDatabase, saveData, Collection);
            Marketplace = new MarketplaceService();

            // Cloud sign-in and save sync run in the background from here (see CloudSyncService),
            // falling back to local-only if the cloud project isn't reachable.
            CloudSync = gameObject.AddComponent<CloudSyncService>();
            CloudSync.BeginSync(saveData, Collection);

            GoogleDesktopAuth.ClientId = googleAuthConfig != null ? googleAuthConfig.clientId : "";
            GoogleDesktopAuth.ClientSecret = googleAuthConfig != null ? googleAuthConfig.clientSecret : "";
        }

        private const string GoogleLinkedPrefKey = "GoogleAccountLinked";

        /// <summary>Local flag for whether Google sign-in ever succeeded on this device - used for
        /// UI only (see ProfileScreen), not an authoritative check of the actual linked state.</summary>
        public bool IsGoogleLinked => PlayerPrefs.GetInt(GoogleLinkedPrefKey, 0) == 1;

        /// <summary>Runs the Google OAuth flow and links the Google identity onto the current
        /// anonymous account, so already-synced progress stays attached instead of starting a fresh
        /// cloud account. If that Google account is already linked to a different profile, Unity
        /// Authentication throws AccountAlreadyLinked - handled by signing into that existing account
        /// instead and re-syncing (see CloudSyncService.ResyncAfterIdentitySwitchAsync). Any other
        /// failure is thrown for the caller to catch and display.</summary>
        public async Task LinkGoogleAccountAsync()
        {
            if (CloudSync == null) throw new InvalidOperationException("Le service cloud n'est pas encore pret.");

            await CloudSync.EnsureSignedInAsync();

            string idToken = await GoogleDesktopAuth.SignInAndGetIdTokenAsync();

            try
            {
                await AuthenticationService.Instance.LinkWithGoogleAsync(idToken);
            }
            catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                AuthenticationService.Instance.SignOut(clearCredentials: true);
                await AuthenticationService.Instance.SignInWithGoogleAsync(idToken);
                await CloudSync.ResyncAfterIdentitySwitchAsync();
            }

            PlayerPrefs.SetInt(GoogleLinkedPrefKey, 1);
            PlayerPrefs.Save();

            // Push the cloud save immediately rather than waiting for the next Save() call.
            await CloudSync.PushNowAsync();
        }

        /// <summary>Resolves which BoosterConfig governs opening a Set: explicit config wins, then
        /// the Set's own boosterConfig, then GameManager's default. Lets each set have its own pack
        /// structure purely from data.</summary>
        public BoosterConfig GetEffectiveBoosterConfig(CardSet set, BoosterConfig explicitConfig = null)
        {
            if (explicitConfig != null) return explicitConfig;
            if (set != null && set.boosterConfig != null) return set.boosterConfig;
            return defaultBoosterConfig;
        }

        /// <summary>Opens a free booster (no currency involved) using the resolved config (see
        /// GetEffectiveBoosterConfig), adds the results to the collection, updates quests, and
        /// saves.</summary>
        public System.Collections.Generic.List<CollectionManager.CardPullResult> OpenBooster(CardSet set, BoosterConfig config = null)
        {
            var effectiveConfig = GetEffectiveBoosterConfig(set, config);
            return OpenBoosterInternal(set, effectiveConfig);
        }

        /// <summary>Buys and opens one booster at the resolved config's purchasePrice. Returns null
        /// and spends nothing if the balance is insufficient; otherwise behaves like
        /// OpenBooster.</summary>
        public System.Collections.Generic.List<CollectionManager.CardPullResult> PurchaseBooster(CardSet set, BoosterConfig config = null)
        {
            var effectiveConfig = GetEffectiveBoosterConfig(set, config);
            if (effectiveConfig == null || !Collection.TrySpendCurrency(effectiveConfig.purchasePrice))
                return null;

            return OpenBoosterInternal(set, effectiveConfig);
        }

        /// <summary>Buys and opens a Bundle (bundleSize boosters at BundlePrice), returning every
        /// pull concatenated. Returns null if this config has no Bundle tier (HasBundle false) or
        /// the balance is insufficient.</summary>
        public System.Collections.Generic.List<CollectionManager.CardPullResult> PurchaseBundle(CardSet set, BoosterConfig config = null)
        {
            var effectiveConfig = GetEffectiveBoosterConfig(set, config);
            if (effectiveConfig == null || !effectiveConfig.HasBundle || !Collection.TrySpendCurrency(effectiveConfig.BundlePrice))
                return null;

            var allResults = new System.Collections.Generic.List<CollectionManager.CardPullResult>();
            for (int i = 0; i < effectiveConfig.bundleSize; i++)
            {
                var cards = BoosterService.OpenBooster(set, cardDatabase, effectiveConfig);
                var results = Collection.AddCards(cards);

                Quests.RegisterBoosterOpened();
                Quests.RegisterPullResults(results);

                allResults.AddRange(results);
            }

            Collection.Save();
            return allResults;
        }

        /// <summary>Buys and opens a full Display (displaySize boosters at DisplayPrice), returning
        /// every pull concatenated. This is the "open all at once" choice on the Display popup - see
        /// PurchaseDisplayBanked for the "one at a time" alternative.</summary>
        public System.Collections.Generic.List<CollectionManager.CardPullResult> PurchaseDisplay(CardSet set, BoosterConfig config = null)
        {
            var effectiveConfig = GetEffectiveBoosterConfig(set, config);
            if (effectiveConfig == null || !Collection.TrySpendCurrency(effectiveConfig.DisplayPrice))
                return null;

            var allResults = new System.Collections.Generic.List<CollectionManager.CardPullResult>();
            for (int i = 0; i < effectiveConfig.displaySize; i++)
            {
                var cards = BoosterService.OpenBooster(set, cardDatabase, effectiveConfig);
                var results = Collection.AddCards(cards);

                Quests.RegisterBoosterOpened();
                Quests.RegisterPullResults(results);

                allResults.AddRange(results);
            }

            Collection.Save();
            return allResults;
        }

        /// <summary>Buys a full Display at DisplayPrice but doesn't open it - credits displaySize
        /// unopened boosters to the Set's banked count instead, to be drained one at a time via
        /// OpenBankedBooster. Returns false and spends nothing if the balance is insufficient.</summary>
        public bool PurchaseDisplayBanked(CardSet set, BoosterConfig config = null)
        {
            var effectiveConfig = GetEffectiveBoosterConfig(set, config);
            if (effectiveConfig == null || !Collection.TrySpendCurrency(effectiveConfig.DisplayPrice))
                return false;

            Collection.AddBankedBoosters(set, effectiveConfig.displaySize);
            Collection.Save();
            return true;
        }

        /// <summary>Opens one already-paid-for banked booster for this Set - no currency spent here.
        /// Returns null if none are banked; otherwise behaves like OpenBooster.</summary>
        public System.Collections.Generic.List<CollectionManager.CardPullResult> OpenBankedBooster(CardSet set, BoosterConfig config = null)
        {
            if (!Collection.TryConsumeBankedBooster(set)) return null;

            var effectiveConfig = GetEffectiveBoosterConfig(set, config);
            return OpenBoosterInternal(set, effectiveConfig);
        }

        /// <summary>Buys today's Boutique offer for the given licence and saves on success. Returns
        /// false if there's no purchasable offer (none generated, already owned, or insufficient
        /// currency).</summary>
        public bool PurchaseShopOffer(License license)
        {
            if (Shop == null || !Shop.TryPurchaseOffer(license)) return false;
            Collection.Save();
            return true;
        }

        private System.Collections.Generic.List<CollectionManager.CardPullResult> OpenBoosterInternal(CardSet set, BoosterConfig config)
        {
            var cards = BoosterService.OpenBooster(set, cardDatabase, config);
            var results = Collection.AddCards(cards);

            Quests.RegisterBoosterOpened();
            Quests.RegisterPullResults(results);

            Collection.Save();
            return results;
        }
    }
}
