using System.Collections.Generic;
using TCGCollector.Data;
using UnityEngine;

namespace TCGCollector.Systems
{
    /// <summary>Loads card artwork on demand from Resources/Cards/&lt;cardId&gt; instead of holding
    /// a direct Sprite reference on CardData (see CardData.artwork, deprecated). A direct field on
    /// CardData would keep its sprite permanently resident once touched, since CardDatabase.allCards
    /// never unloads for the session - memory would only ever grow. Acquire()/Release() use
    /// Resources.Load + a refcount instead, so a card's art can actually be freed once nothing is
    /// showing it. Every successful Acquire MUST be paired with exactly one Release, or this
    /// recreates the same never-frees-memory problem (see BinderScreen/BoosterOpenScreen for the
    /// pattern).</summary>
    public static class CardArtworkLoader
    {
        private class Entry
        {
            public Sprite Sprite;
            public int RefCount;
        }

        private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

        // Resources.UnloadUnusedAssets scans the whole asset graph, so it's too slow to call on
        // every Release - callers batch it via RequestSweep() at natural "done with a batch" points.
        private static bool _sweepPending;

        // Extra throttle: a fast scroll through a virtualized grid can call RequestSweep() many
        // times per second, and each full UnloadUnusedAssets() pass is heavy enough to cause frame
        // hitches. The actual per-texture memory is already freed by UnloadAsset() in Release();
        // this sweep is just a periodic safety net, safe to defer.
        private static float _lastSweepTime = -999f;
        private const float MinSweepIntervalSeconds = 1f;

        /// <summary>Loads (or reuses) a card's artwork and increments its refcount. Returns null if
        /// the card has no art in Resources/Cards (show the placeholder). Every non-null return
        /// MUST be paired with a later Release(card).</summary>
        public static Sprite Acquire(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.cardId)) return null;

            if (_cache.TryGetValue(card.cardId, out var entry))
            {
                entry.RefCount++;
                return entry.Sprite;
            }

            var sprite = Resources.Load<Sprite>("Cards/" + card.cardId);
            if (sprite == null) return null;

            _cache[card.cardId] = new Entry { Sprite = sprite, RefCount = 1 };
            return sprite;
        }

        /// <summary>Call exactly once for every successful Acquire(card). Safe to call on a card
        /// that was never Acquired (no-op). Callers must clear any Image.sprite still pointing at
        /// the released sprite BEFORE calling this (see UIFactory.SetArtwork(cell, null)) - a live
        /// reference to an unloaded sprite is left broken.</summary>
        public static void Release(CardData card)
        {
            if (card == null || string.IsNullOrEmpty(card.cardId)) return;
            if (!_cache.TryGetValue(card.cardId, out var entry)) return;

            entry.RefCount--;
            if (entry.RefCount <= 0)
            {
                _cache.Remove(card.cardId);
                Resources.UnloadAsset(entry.Sprite);
                _sweepPending = true;
            }
        }

        /// <summary>Sweeps up anything Release() unloaded since the last sweep. Call after a
        /// natural "settled" point rather than after every Release. Throttled to at most once every
        /// MinSweepIntervalSeconds - a call before that stays pending and fires on the next call.</summary>
        public static void RequestSweep()
        {
            if (!_sweepPending) return;
            if (Time.unscaledTime - _lastSweepTime < MinSweepIntervalSeconds) return;
            _sweepPending = false;
            _lastSweepTime = Time.unscaledTime;
            Resources.UnloadUnusedAssets();
        }
    }
}
