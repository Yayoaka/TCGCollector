using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TCGCollector.Save;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>Cloud sync: anonymous auth + a single JSON blob in Unity Cloud Save, "newest wins"
    /// against PlayerSaveData.lastModifiedUtcTicks. Entirely best-effort - every step is wrapped in
    /// try/catch so a missing cloud project, no network, or a services outage just leaves the
    /// player on their local save instead of blocking startup.
    ///
    /// GameManager.Awake() boots from the local save as normal, then calls BeginSync once
    /// Collection exists; sign-in and the cloud round-trip happen in the background. If the cloud
    /// copy is newer, PlayerSaveData.CopyFrom overwrites the shared save object's fields in place
    /// (every system already holds a reference to that object) and
    /// CollectionManager.RebuildLookupsFromSaveData() rebuilds its dictionaries - no screen needs
    /// to be told to reload.
    ///
    /// Scope for this phase: anonymous auth only (real Google sign-in is out of scope for now),
    /// and a single whole-save blob (no per-field merge).</summary>
    /// <summary>Wrapper for the two fields with their own dedicated Cloud Save keys (see the class
    /// comment) - JsonUtility needs a real type to (de)serialize a bare List.</summary>
    [Serializable]
    internal class OwnedCardsWrapper
    {
        public List<OwnedCardEntry> entries = new List<OwnedCardEntry>();
    }

    public class CloudSyncService : MonoBehaviour
    {
        private const string SaveKey = "player_save";

        // currency/ownedCards are pulled OUT of the whole-blob save and given their own dedicated
        // Cloud Save keys. Why: the marketplace runs through Cloud Code, which can change ANOTHER
        // player's currency/cards server-side (e.g. crediting a seller instantly on a sale) while
        // that player's own client is offline. If those fields still lived in "player_save", this
        // client's next whole-blob push would silently overwrite what Cloud Code just wrote - the
        // whole-blob "newest wins" scheme has no way to compare against a change it didn't make
        // itself. Splitting them out means Cloud Code only touches these two keys, and pulling them
        // is a cloud-value-always-wins read (see PullAndMergeAsync), not a timestamp comparison.
        //
        // Still best-effort, not server-authoritative: normal local changes only reach the cloud on
        // this client's next pull+push cycle (see RunAsync), so two devices on the same account, or
        // a marketplace sale landing in that gap, can still race. Acceptable at this scale; a full
        // fix would route every currency/card change through Cloud Code, not just trades.
        private const string CurrencyKey = "player_currency";
        private const string OwnedCardsKey = "player_owned_cards";

        public enum Status { Idle, Connecting, SignedIn, Synced, Offline, Error }

        public static CloudSyncService Instance { get; private set; }

        public Status CurrentStatus { get; private set; } = Status.Idle;

        /// <summary>Fired whenever CurrentStatus changes - a UI indicator (see ProfileScreen) can
        /// subscribe, or just re-read CurrentStatus next refresh.</summary>
        public static event Action<Status> OnStatusChanged;

        /// <summary>Fired once, only if a newer cloud save was applied over the local one - lets an
        /// already-open screen know its data changed under it.</summary>
        public static event Action OnCloudDataApplied;

        private PlayerSaveData _saveData;
        private CollectionManager _collection;
        private bool _started;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        /// <summary>Kicks off sign-in + cloud sync in the background. Safe to call more than once;
        /// only the first call does anything.</summary>
        public void BeginSync(PlayerSaveData saveData, CollectionManager collection)
        {
            if (_started) return;
            _started = true;

            _saveData = saveData;
            _collection = collection;

            _ = RunAsync();
        }

        private void SetStatus(Status status)
        {
            CurrentStatus = status;
            OnStatusChanged?.Invoke(status);
        }

        private async Task RunAsync()
        {
            SetStatus(Status.Connecting);
            try
            {
                await EnsureSignedInAsync();
                SetStatus(Status.SignedIn);

                await PullAndMergeAsync();
                await PushAsync();

                SetStatus(Status.Synced);
            }
            catch (Exception e)
            {
                // Anything here just means "stay on the local save" - never worth interrupting the player over.
                Debug.LogWarning($"[CloudSyncService] Cloud sync unavailable, staying local-only: {e.Message}");
                SetStatus(Status.Offline);
            }
        }

        /// <summary>Makes sure UGS is initialized and the player is signed in (anonymously by
        /// default). Idempotent. Split out of RunAsync so GameManager.LinkGoogleAccountAsync can
        /// await it before linking a Google identity, in case the player links before the
        /// background sync's own first sign-in has finished.</summary>
        public async Task EnsureSignedInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        /// <summary>Pushes the current save to the cloud right away, outside the normal background
        /// cadence - used right after linking a Google account so its cloud copy is current
        /// immediately.</summary>
        public async Task PushNowAsync()
        {
            if (_saveData == null) return;
            await PushAsync();
        }

        /// <summary>Re-runs the pull+merge+push cycle against whichever identity is currently
        /// signed in - used by GameManager.LinkGoogleAccountAsync's AccountAlreadyLinked fallback
        /// after switching from the fresh anonymous session to the pre-existing account. Same
        /// "newest wins" comparison as the normal boot-time sync.</summary>
        public async Task ResyncAfterIdentitySwitchAsync()
        {
            if (_saveData == null) return;
            await PullAndMergeAsync();
            await PushAsync();
            SetStatus(Status.Synced);
        }

        private async Task PullAndMergeAsync()
        {
            var result = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { SaveKey, CurrencyKey, OwnedCardsKey });

            bool changed = false;

            // Whole-blob merge - everything except currency/ownedCards (handled below via their
            // own keys). "Newest wins" is meaningful here since this blob is only ever written by
            // this player's own client, unlike the Cloud-Code-writable fields below.
            if (result.TryGetValue(SaveKey, out var blobItem))
            {
                string json = blobItem.Value.GetAs<string>();
                if (!string.IsNullOrEmpty(json))
                {
                    PlayerSaveData cloudData = null;
                    try
                    {
                        cloudData = JsonUtility.FromJson<PlayerSaveData>(json);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CloudSyncService] Couldn't parse the cloud save, ignoring it: {e.Message}");
                    }

                    if (cloudData != null && cloudData.lastModifiedUtcTicks > _saveData.lastModifiedUtcTicks)
                    {
                        // CopyFrom also copies cloudData's (possibly stale) currency/ownedCards -
                        // harmless, since the dedicated-key reads below overwrite them anyway.
                        _saveData.CopyFrom(cloudData);
                        changed = true;
                    }
                }
            }

            // Currency/ownedCards: the cloud's dedicated-key value always wins when present, no
            // timestamp comparison (see class comment). Absent entirely just means neither this
            // client nor Cloud Code has written it yet - keep the local value.
            if (result.TryGetValue(CurrencyKey, out var currencyItem))
            {
                int cloudCurrency = currencyItem.Value.GetAs<int>();
                if (cloudCurrency != _saveData.currency)
                {
                    _saveData.currency = cloudCurrency;
                    changed = true;
                }
            }

            if (result.TryGetValue(OwnedCardsKey, out var cardsItem))
            {
                string cardsJson = cardsItem.Value.GetAs<string>();
                if (!string.IsNullOrEmpty(cardsJson))
                {
                    try
                    {
                        var wrapper = JsonUtility.FromJson<OwnedCardsWrapper>(cardsJson);
                        if (wrapper != null)
                        {
                            _saveData.ownedCards = wrapper.entries ?? new List<OwnedCardEntry>();
                            changed = true;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CloudSyncService] Couldn't parse cloud owned cards, ignoring: {e.Message}");
                    }
                }
            }

            if (changed)
            {
                _collection.RebuildLookupsFromSaveData();
                SaveManager.Save(_saveData); // mirror the merge to disk, not just in memory
                OnCloudDataApplied?.Invoke();
            }
        }

        private async Task PushAsync()
        {
            string json = JsonUtility.ToJson(_saveData);
            string cardsJson = JsonUtility.ToJson(new OwnedCardsWrapper { entries = _saveData.ownedCards });
            var data = new Dictionary<string, object>
            {
                { SaveKey, json },
                { CurrencyKey, _saveData.currency },
                { OwnedCardsKey, cardsJson },
            };
            await CloudSaveService.Instance.Data.Player.SaveAsync(data);
        }

        /// <summary>On-demand refresh of just currency/ownedCards (cheap - two small keys, not the
        /// whole blob) - call after a marketplace action or whenever a screen wants the latest
        /// server-side truth without waiting for the next full sync.</summary>
        public async Task<bool> RefreshCurrencyAndCardsAsync()
        {
            if (_saveData == null) return false;
            try
            {
                await EnsureSignedInAsync();

                var result = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { CurrencyKey, OwnedCardsKey });
                bool changed = false;

                if (result.TryGetValue(CurrencyKey, out var currencyItem))
                {
                    int cloudCurrency = currencyItem.Value.GetAs<int>();
                    if (cloudCurrency != _saveData.currency)
                    {
                        _saveData.currency = cloudCurrency;
                        changed = true;
                    }
                }

                if (result.TryGetValue(OwnedCardsKey, out var cardsItem))
                {
                    string cardsJson = cardsItem.Value.GetAs<string>();
                    if (!string.IsNullOrEmpty(cardsJson))
                    {
                        var wrapper = JsonUtility.FromJson<OwnedCardsWrapper>(cardsJson);
                        if (wrapper != null)
                        {
                            _saveData.ownedCards = wrapper.entries ?? new List<OwnedCardEntry>();
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    _collection.RebuildLookupsFromSaveData();
                    SaveManager.Save(_saveData);
                    OnCloudDataApplied?.Invoke();
                }

                return changed;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CloudSyncService] RefreshCurrencyAndCardsAsync failed: {e.Message}");
                return false;
            }
        }
    }
}
