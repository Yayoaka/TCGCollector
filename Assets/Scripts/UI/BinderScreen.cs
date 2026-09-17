using System.Collections;
using System.Collections.Generic;
using TCGCollector.Core;
using TCGCollector.Data;
using TCGCollector.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.UI
{
    /// <summary>
    /// The "classeur": every card in the database, filterable by License, Set, Rarity and a live
    /// text search, with a completion percentage. Filtering cascades License -> Set -> Rarity:
    /// picking a License rebuilds the Set row and auto-selects its first Set, which rebuilds the
    /// Rarity row in turn. Sets with no License are grouped under a synthetic "Autres" bucket
    /// (see GetOrphanSets) instead of vanishing from the picker.
    ///
    /// Expects a GameObject with this child hierarchy (built by UISceneBuilder.BuildBinderScreen):
    ///   Header (Text)
    ///   SetFilterRow / RarityFilterRow (empty containers with HorizontalLayoutGroup, converted at
    ///     runtime into scrollable strips by UIFactory.MakeHorizontallyScrollable - see Awake())
    ///   CardGrid (built via UIFactory.CreateVerticalScrollGrid -> "CardGrid/Viewport/Content")
    /// A LicenseFilterRow and a search bar are added purely at runtime (see BuildLicenseFilterRow/
    /// BuildSearchBar), shifting the bands above down to make room.
    ///
    /// Tapping a card cell shows it big on screen via a lazily-built overlay (see
    /// BuildBigCardOverlayIfNeeded).
    ///
    /// The grid is virtualized: with 1000+ cards, instantiating/destroying a full cell per card on
    /// every keystroke is too much GC churn. Instead Content keeps a small fixed pool of cells
    /// (see EnsurePoolSized) that just get repositioned/rebound to new card data as the player
    /// scrolls or filters (see RebindVisibleWindow/BindSlot), replacing the GridLayoutGroup +
    /// ContentSizeFitter that CreateVerticalScrollGrid normally relies on.
    /// </summary>
    public class BinderScreen : MonoBehaviour
    {
        private static readonly Vector2 CellSize = new Vector2(300, 420);
        private static readonly Vector2 BigCellSize = new Vector2(700, 860);
        private static readonly Vector2 RarityButtonCellSize = new Vector2(180, 60);
        private static readonly Vector2 SetButtonCellSize = new Vector2(220, 70);
        private static readonly Vector2 LicenseButtonCellSize = new Vector2(220, 70);
        private static readonly Color OwnedFallbackColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color UnownedColor = new Color(0.15f, 0.15f, 0.15f);
        private static readonly Color FilterDefaultColor = new Color(0.25f, 0.25f, 0.3f);
        private static readonly Color FilterSelectedColor = new Color(0.35f, 0.55f, 0.9f);
        private static readonly Color BigCardOverlayBackground = new Color(0f, 0f, 0f, 0.75f);

        // Binder-only display grouping: rarities that are mechanically distinct elsewhere (odds,
        // sell value) visually collapse into a broader bucket just for browsing here. Only affects
        // RarityLabel() below, not CardRarity itself.
        private static readonly Dictionary<string, string> BinderRarityLabelOverride = new Dictionary<string, string>
        {
            { "Rune Alt", "Alt Art" },
            { "Token", "Commune" },
            { "Rune", "Commune" },
        };

        // Must match the (cellSize, spacing, columns) UISceneBuilder passed to
        // CreateVerticalScrollGrid for "CardGrid", since the pool now reproduces that layout by
        // hand instead of letting GridLayoutGroup compute it.
        private const int Columns = 3;
        private static readonly Vector2 GridSpacing = new Vector2(20, 20);
        // Plain ints instead of a static RectOffset - Unity forbids calling RectOffset's setters
        // outside Awake/Start/instance code, before which a static readonly field is initialized.
        private const int GridPaddingLeft = 20;
        private const int GridPaddingRight = 20;
        private const int GridPaddingTop = 20;
        private const int GridPaddingBottom = 20;

        // Extra pooled rows kept alive above/below the viewport so a fast scroll flick doesn't
        // show a blank frame while cells catch up.
        private const int BufferRowsEachSide = 2;
        // Floor for pool sizing in case the viewport's rect.height still reads 0 the first time
        // EnsurePoolSized runs (not laid out yet).
        private const int MinVisibleRows = 5;

        private Text _header;
        private Transform _licenseFilterRow;
        private Transform _setFilterRow;
        private Transform _rarityFilterRow;
        private Transform _gridContent;
        private InputField _searchInput;
        private string _searchQuery = "";

        private GameObject _bigCardOverlay;
        private UIFactory.CardCellRefs _bigCell;

        // null = either "nothing selected yet" or the orphan bucket - see _selectedIsOrphanBucket.
        private License _selectedLicense;
        private bool _selectedIsOrphanBucket;

        private CardSet _selectedSet;

        // Filtered by label (after BinderRarityLabelOverride), not by CardRarity reference, since
        // different sets each have their own CardRarity asset for the same tier name - filtering
        // by label avoids duplicate "Commune" tabs, one per set's asset.
        private string _selectedRarityLabel; // null = all rarities

        private bool _filtersBuilt;

        // The current Set/Rarity/search-filtered card list, recomputed by Refresh(). Just data -
        // no GameObjects are created per entry (see the pool below).
        private readonly List<CardData> _filteredCards = new List<CardData>();

        // Grow-only pool of live cells backing the grid (see EnsurePoolSized) - each Cell is
        // created once and reused; only bound data, position and artwork change on scroll/filter.
        private readonly List<PooledCell> _pool = new List<PooledCell>();
        // Topmost row currently represented by the pool - RebindVisibleWindow only rebinds when
        // this changes. -1 forces a rebind regardless.
        private int _lastWindowTopRow = -1;

        private CardData _bigCardArtworkBound;

        // license == null with isOrphan == true is the synthetic "Autres" bucket - see
        // GetOrphanSets/CreateLicenseFilterButton.
        private readonly List<(License license, bool isOrphan, Button button)> _licenseFilterButtons = new List<(License, bool, Button)>();
        // Scoped to whichever License/orphan bucket is selected - destroyed and rebuilt every time
        // SelectLicense runs.
        private readonly List<(CardSet set, Button button)> _setFilterButtons = new List<(CardSet, Button)>();
        private readonly List<Button> _rarityFilterButtons = new List<Button>();
        // Parallel to _rarityFilterButtons - index 0 is always null (the "Toutes" button).
        private readonly List<string> _rarityButtonLabels = new List<string>();

        /// <summary>One pooled grid cell: the live UI, which filtered-list index (if any) it shows,
        /// and which card's artwork it holds an Acquire() on (so Release/Acquire stays balanced
        /// even though the cell itself is never destroyed).</summary>
        private class PooledCell
        {
            public UIFactory.CardCellRefs Cell;
            public int BoundIndex = -1;
            public CardData BoundArtworkCard;
        }

        private void Awake()
        {
            _header = transform.Find("Header").GetComponent<Text>();

            // Scene-baked bands only leave room for Header + SetFilterRow + RarityFilterRow -
            // shifted down here to fit a new LicenseFilterRow above SetFilterRow.
            _licenseFilterRow = BuildLicenseFilterRow();

            // SetFilterRow's scene-baked HorizontalLayoutGroup overflows once enough sets are
            // registered, squeezing/truncating labels - retrofit horizontal scrolling instead.
            var setRowRt = (RectTransform)transform.Find("SetFilterRow");
            setRowRt.anchorMin = new Vector2(0f, 0.78f);
            setRowRt.anchorMax = new Vector2(1f, 0.85f);
            _setFilterRow = UIFactory.MakeHorizontallyScrollable(setRowRt, SetButtonCellSize, new Vector2(12, 0));

            // Same overflow problem for RarityFilterRow - retrofit scrolling here too rather than
            // rebuilding the saved scene.
            var rarityRowRt = (RectTransform)transform.Find("RarityFilterRow");
            rarityRowRt.anchorMin = new Vector2(0f, 0.71f);
            rarityRowRt.anchorMax = new Vector2(1f, 0.78f);
            _rarityFilterRow = UIFactory.MakeHorizontallyScrollable(rarityRowRt, RarityButtonCellSize, new Vector2(12, 0));

            _gridContent = transform.Find("CardGrid/Viewport/Content");

            // CreateVerticalScrollGrid gave Content a GridLayoutGroup + ContentSizeFitter that
            // auto-arranges/auto-sizes for all children - wrong here where only a small pool of
            // cells exists at any time. Strip both; BindSlot/LayoutContentHeight take over manually.
            foreach (var layoutGroup in _gridContent.GetComponents<LayoutGroup>())
                Destroy(layoutGroup);
            var fitter = _gridContent.GetComponent<ContentSizeFitter>();
            if (fitter != null) Destroy(fitter);

            // ResponsiveCardGrid [RequireComponent]s the GridLayoutGroup just destroyed and reads
            // it every frame, so it would throw continuously once we take over with manual
            // positioning. This screen always uses a fixed CellSize anyway, so strip it too.
            var responsiveGrid = _gridContent.GetComponent<ResponsiveCardGrid>();
            if (responsiveGrid != null) Destroy(responsiveGrid);

            // Content -> Viewport -> CardGrid (the ScrollRect owner).
            var scrollRect = _gridContent.parent.parent.GetComponent<ScrollRect>();
            scrollRect.onValueChanged.AddListener(_ => RebindVisibleWindow(force: false));

            // CardGrid's baked-in band is compacted further at runtime to free room for
            // LicenseFilterRow and the search bar (see BuildSearchBar).
            var gridRootRt = (RectTransform)transform.Find("CardGrid");
            gridRootRt.anchorMin = new Vector2(0f, 0f);
            gridRootRt.anchorMax = new Vector2(1f, 0.64f);

            BuildSearchBar();
        }

        /// <summary>Builds the License row at runtime, just above SetFilterRow, scrollable like the
        /// other filter rows since it can overflow too. Returns the Content transform new buttons
        /// should be parented to (see CreateLicenseFilterButton).</summary>
        private Transform BuildLicenseFilterRow()
        {
            var rowGO = new GameObject("LicenseFilterRow", typeof(RectTransform));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(transform, false);
            rowRt.anchorMin = new Vector2(0f, 0.85f);
            rowRt.anchorMax = new Vector2(1f, 0.92f);
            rowRt.offsetMin = Vector2.zero;
            rowRt.offsetMax = Vector2.zero;

            return UIFactory.MakeHorizontallyScrollable(rowRt, LicenseButtonCellSize, new Vector2(12, 0));
        }

        /// <summary>Builds a text search bar between RarityFilterRow and CardGrid, filtering the
        /// grid live as the player types (see OnSearchChanged/Refresh). Matches on displayName
        /// even for unowned cards, so a hunted card still shows up (as "???").</summary>
        private void BuildSearchBar()
        {
            var rowGO = new GameObject("SearchRow", typeof(RectTransform));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(transform, false);
            rowRt.anchorMin = new Vector2(0f, 0.64f);
            rowRt.anchorMax = new Vector2(1f, 0.71f);
            rowRt.offsetMin = new Vector2(20, 6);
            rowRt.offsetMax = new Vector2(-20, -6);

            _searchInput = UIFactory.CreateInputField(rowRt, "Rechercher une carte...");
            var searchRt = (RectTransform)_searchInput.transform;
            searchRt.anchorMin = Vector2.zero;
            searchRt.anchorMax = Vector2.one;
            searchRt.offsetMin = Vector2.zero;
            searchRt.offsetMax = Vector2.zero;

            _searchInput.onValueChanged.AddListener(OnSearchChanged);
        }

        private void OnSearchChanged(string value)
        {
            _searchQuery = value;
            Refresh();
        }

        private bool CardMatchesSearch(CardData card)
        {
            if (string.IsNullOrEmpty(card.displayName)) return false;
            return card.displayName.IndexOf(_searchQuery, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnEnable()
        {
            StartCoroutine(WaitForGameManagerThenRefresh());
        }

        private IEnumerator WaitForGameManagerThenRefresh()
        {
            while (GameManager.Instance == null || GameManager.Instance.CardDatabase == null)
                yield return null;

            if (!_filtersBuilt) BuildFilters();
            Refresh();
        }

        /// <summary>Builds the License row (one button per License with cards, plus a synthetic
        /// "Autres" bucket for orphan Sets - see GetOrphanSets) and auto-selects the first one,
        /// cascading into the Set and Rarity rows (see SelectLicense/SelectSet).</summary>
        private void BuildFilters()
        {
            _filtersBuilt = true;
            var db = GameManager.Instance.CardDatabase;

            var licenses = db.GetAllLicenses();
            var orphanSets = GetOrphanSets(db);

            foreach (var license in licenses)
            {
                var label = string.IsNullOrEmpty(license.displayName) ? license.name : license.displayName;
                CreateLicenseFilterButton(license, false, label);
            }

            if (orphanSets.Count > 0)
                CreateLicenseFilterButton(null, true, "Autres");

            if (licenses.Count > 0)
                SelectLicense(licenses[0], false);
            else if (orphanSets.Count > 0)
                SelectLicense(null, true);
            else
                _header.text = "Aucune Licence ni Set trouve - verifie la Card Database.";
        }

        /// <summary>Sets with no License assigned - grouped under the synthetic "Autres" bucket
        /// (see BuildFilters/CreateLicenseFilterButton) instead of vanishing from the picker.</summary>
        private static List<CardSet> GetOrphanSets(CardDatabase db)
        {
            var orphans = new List<CardSet>();
            foreach (var set in db.GetAllSets())
                if (set != null && set.license == null) orphans.Add(set);

            orphans.Sort((a, b) => a.sortOrder.CompareTo(b.sortOrder));
            return orphans;
        }

        private static string RarityLabel(CardRarity rarity)
        {
            string raw = string.IsNullOrEmpty(rarity.displayName) ? rarity.name : rarity.displayName;
            return BinderRarityLabelOverride.TryGetValue(raw, out var merged) ? merged : raw;
        }

        /// <summary>Rebuilds the rarity buttons for the currently selected Set: one button per
        /// distinct rarity LABEL (not per CardRarity asset), so sets sharing a tier name collapse
        /// into one button. Called at startup and whenever the License or Set filter changes.</summary>
        private void RebuildRarityFilters()
        {
            var db = GameManager.Instance.CardDatabase;

            foreach (var button in _rarityFilterButtons)
                Destroy(button.gameObject);
            _rarityFilterButtons.Clear();
            _rarityButtonLabels.Clear();

            // label -> first CardRarity seen with that label, kept only to read its .rank for sort order.
            var labelToRarity = new Dictionary<string, CardRarity>();
            foreach (var card in db.allCards)
            {
                if (card == null || card.rarity == null) continue;
                if (_selectedSet != null && card.set != _selectedSet) continue;

                string label = RarityLabel(card.rarity);
                // Keep the LOWEST rank among CardRarities sharing this label, not just the first
                // seen, so a merged bucket sorts at its lowest-ranked member's position.
                if (!labelToRarity.TryGetValue(label, out var existing) || card.rarity.rank < existing.rank)
                    labelToRarity[label] = card.rarity;
            }

            var orderedLabels = new List<string>(labelToRarity.Keys);
            orderedLabels.Sort((a, b) => labelToRarity[a].rank.CompareTo(labelToRarity[b].rank));

            CreateRarityFilterButton("Toutes", null);
            foreach (var label in orderedLabels)
                CreateRarityFilterButton(label, label);

            // The previously-selected rarity may not exist for the new set - reset to "Toutes".
            _selectedRarityLabel = null;
        }

        private void CreateLicenseFilterButton(License license, bool isOrphan, string label)
        {
            var button = UIFactory.CreateButton(_licenseFilterRow, label, FilterDefaultColor, Color.white, () => SelectLicense(license, isOrphan));
            _licenseFilterButtons.Add((license, isOrphan, button));
        }

        /// <summary>Rebuilds the Set row for the chosen License (or "Autres" bucket) and
        /// auto-selects its first Set.</summary>
        private void SelectLicense(License license, bool isOrphan)
        {
            _selectedLicense = license;
            _selectedIsOrphanBucket = isOrphan;

            var db = GameManager.Instance.CardDatabase;
            var setsInLicense = isOrphan ? GetOrphanSets(db) : db.GetSetsInLicense(license);

            foreach (var (_, button) in _setFilterButtons)
                Destroy(button.gameObject);
            _setFilterButtons.Clear();

            foreach (var set in setsInLicense)
                CreateSetFilterButton(set, string.IsNullOrEmpty(set.displayName) ? set.name : set.displayName);

            // Every License (and the "Autres" bucket, when built - see BuildFilters) is guaranteed
            // to have at least one Set with cards, so setsInLicense is never empty here.
            SelectSet(setsInLicense[0]);
        }

        private void CreateSetFilterButton(CardSet set, string label)
        {
            var capturedSet = set;
            var button = UIFactory.CreateButton(_setFilterRow, label, FilterDefaultColor, Color.white, () => SelectSet(capturedSet));
            _setFilterButtons.Add((capturedSet, button));
        }

        /// <summary>Rebuilds the Rarity row for the chosen Set and refreshes the grid.</summary>
        private void SelectSet(CardSet set)
        {
            _selectedSet = set;
            RebuildRarityFilters();
            UpdateFilterHighlights();
            Refresh();
        }

        private void CreateRarityFilterButton(string displayLabel, string selectionValue)
        {
            var button = UIFactory.CreateButton(_rarityFilterRow, displayLabel, FilterDefaultColor, Color.white, () =>
            {
                _selectedRarityLabel = selectionValue;
                UpdateFilterHighlights();
                Refresh();
            });
            _rarityFilterButtons.Add(button);
            _rarityButtonLabels.Add(selectionValue);
        }

        private void UpdateFilterHighlights()
        {
            foreach (var (buttonLicense, buttonIsOrphan, button) in _licenseFilterButtons)
                button.GetComponent<Image>().color = (buttonLicense == _selectedLicense && buttonIsOrphan == _selectedIsOrphanBucket) ? FilterSelectedColor : FilterDefaultColor;

            foreach (var (set, button) in _setFilterButtons)
                button.GetComponent<Image>().color = set == _selectedSet ? FilterSelectedColor : FilterDefaultColor;

            for (int i = 0; i < _rarityFilterButtons.Count; i++)
            {
                string buttonLabel = _rarityButtonLabels[i];
                _rarityFilterButtons[i].GetComponent<Image>().color = buttonLabel == _selectedRarityLabel ? FilterSelectedColor : FilterDefaultColor;
            }
        }

        /// <summary>Recomputes the filtered card list (Set/Rarity/search) and re-lays-out the
        /// virtualized grid. Called on every search keystroke and filter click - cheap since it
        /// only touches the small pool (see RebindVisibleWindow).</summary>
        public void Refresh()
        {
            if (GameManager.Instance == null || GameManager.Instance.CardDatabase == null) return;

            var db = GameManager.Instance.CardDatabase;
            var collection = GameManager.Instance.Collection;

            _filteredCards.Clear();
            int shownOwned = 0;

            foreach (var card in db.allCards)
            {
                if (card == null) continue;
                if (_selectedSet != null && card.set != _selectedSet) continue;
                if (_selectedRarityLabel != null && (card.rarity == null || RarityLabel(card.rarity) != _selectedRarityLabel)) continue;
                if (!string.IsNullOrEmpty(_searchQuery) && !CardMatchesSearch(card)) continue;

                _filteredCards.Add(card);
                if (collection.GetOwnedCount(card) > 0) shownOwned++;
            }

            string setLabel = _selectedSet != null
                ? (string.IsNullOrEmpty(_selectedSet.displayName) ? _selectedSet.name : _selectedSet.displayName)
                : "Toute la collection";

            float ratio = _filteredCards.Count == 0 ? 0f : (float)shownOwned / _filteredCards.Count;
            _header.text = $"{setLabel} - {shownOwned}/{_filteredCards.Count} ({ratio:P0}) - {collection.Currency} po";

            // The list just changed size/contents - scroll back to the top rather than leaving the
            // view past the end of a now-shorter list.
            var contentRectForScrollReset = (RectTransform)_gridContent;
            contentRectForScrollReset.anchoredPosition = new Vector2(contentRectForScrollReset.anchoredPosition.x, 0f);

            LayoutContentHeight();
            // Force a full rebind even if row 0 is still topmost - its cards almost certainly changed.
            _lastWindowTopRow = -1;
            RebindVisibleWindow(force: true);
        }

        /// <summary>Sizes Content's height to match the full filtered list, as if every card had a
        /// real cell laid out by a GridLayoutGroup - only the RectTransform grows/shrinks; the
        /// pooled children are positioned separately by BindSlot.</summary>
        private void LayoutContentHeight()
        {
            int totalRows = _filteredCards.Count == 0 ? 0 : Mathf.CeilToInt((float)_filteredCards.Count / Columns);
            float height = GridPaddingTop + GridPaddingBottom;
            if (totalRows > 0) height += totalRows * CellSize.y + (totalRows - 1) * GridSpacing.y;

            var contentRt = (RectTransform)_gridContent;
            contentRt.sizeDelta = new Vector2(contentRt.sizeDelta.x, height);
        }

        /// <summary>Grows the pool (never shrinks it) to cover the visible viewport plus a small
        /// scroll buffer. Cheap to call often - a no-op once big enough.</summary>
        private void EnsurePoolSized()
        {
            var viewportRt = (RectTransform)_gridContent.parent;
            float viewportHeight = viewportRt.rect.height;
            float rowHeight = CellSize.y + GridSpacing.y;

            int visibleRows = viewportHeight > 0f ? Mathf.CeilToInt(viewportHeight / rowHeight) : MinVisibleRows;
            int poolRows = Mathf.Max(MinVisibleRows, visibleRows) + BufferRowsEachSide * 2;
            int desiredPoolSize = poolRows * Columns;

            while (_pool.Count < desiredPoolSize)
            {
                var cell = UIFactory.CreateCardCell(_gridContent, CellSize);
                var rt = (RectTransform)cell.Root.transform;
                // Top-left anchored/pivoted so BindSlot can position with a simple (x, -y) offset
                // from Content's top-left corner.
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                cell.Root.SetActive(false);
                _pool.Add(new PooledCell { Cell = cell });
            }
        }

        /// <summary>The heart of the virtualization: figures out which row is topmost in view (from
        /// Content's scroll offset) and, only when that changed (or 'force' is set), rebinds every
        /// pooled cell to its new row/column/card. Cheap even when it runs - the pool is sized in
        /// the dozens, not the thousands.</summary>
        private void RebindVisibleWindow(bool force)
        {
            EnsurePoolSized();

            float rowHeight = CellSize.y + GridSpacing.y;
            // Content is anchored top-stretch pivoted on its top edge, so scrolling DOWN makes
            // anchoredPosition.y go POSITIVE, not negative - negating it here used to clamp
            // scrollOffset to 0 past the top, freezing the window and capping the binder at
            // whatever the initial pool covered.
            float scrollOffset = Mathf.Max(0f, ((RectTransform)_gridContent).anchoredPosition.y);
            int windowTopRow = Mathf.Max(0, Mathf.FloorToInt((scrollOffset - GridPaddingTop) / rowHeight) - BufferRowsEachSide);

            if (!force && windowTopRow == _lastWindowTopRow) return;
            _lastWindowTopRow = windowTopRow;

            for (int slotIndex = 0; slotIndex < _pool.Count; slotIndex++)
            {
                int row = windowTopRow + slotIndex / Columns;
                int col = slotIndex % Columns;
                int cardIndex = row * Columns + col;

                if (cardIndex < _filteredCards.Count)
                    BindSlot(_pool[slotIndex], cardIndex, row, col);
                else
                    HideSlot(_pool[slotIndex]);
            }

            CardArtworkLoader.RequestSweep();
        }

        /// <summary>Binds one pooled cell to card #cardIndex of the current filtered list and
        /// positions it at (row, col) in the virtual grid.</summary>
        private void BindSlot(PooledCell slot, int cardIndex, int row, int col)
        {
            var card = _filteredCards[cardIndex];

            ReleaseSlotArtwork(slot);

            var collection = GameManager.Instance.Collection;
            int owned = collection.GetOwnedCount(card);
            bool isOwned = owned > 0;

            Sprite artwork = null;
            if (isOwned)
            {
                artwork = CardArtworkLoader.Acquire(card);
                if (artwork != null) slot.BoundArtworkCard = card;
            }

            ApplyCardToCell(slot.Cell, card, isOwned, owned, artwork);

            var capturedCard = card;
            var capturedIsOwned = isOwned;
            var capturedOwned = owned;
            slot.Cell.RootButton.onClick.RemoveAllListeners();
            slot.Cell.RootButton.onClick.AddListener(() => ShowBigCard(capturedCard, capturedIsOwned, capturedOwned));

            var rt = (RectTransform)slot.Cell.Root.transform;
            rt.anchoredPosition = new Vector2(
                GridPaddingLeft + col * (CellSize.x + GridSpacing.x),
                -(GridPaddingTop + row * (CellSize.y + GridSpacing.y)));

            slot.Cell.Root.SetActive(true);
            slot.BoundIndex = cardIndex;
        }

        /// <summary>Hides a pooled cell that no longer maps to any card in the current filtered
        /// list (e.g. the pool is bigger than the list, or this slot's row fell past the end).</summary>
        private void HideSlot(PooledCell slot)
        {
            if (slot.BoundIndex == -1) return;

            ReleaseSlotArtwork(slot);
            slot.Cell.RootButton.onClick.RemoveAllListeners();
            slot.Cell.Root.SetActive(false);
            slot.BoundIndex = -1;
        }

        /// <summary>Clears a pooled slot's artwork, if it holds one - the cell is reused rather
        /// than destroyed, so nothing else ever drops the reference for us (see CardArtworkLoader).</summary>
        private void ReleaseSlotArtwork(PooledCell slot)
        {
            if (slot.BoundArtworkCard == null) return;
            UIFactory.SetArtwork(slot.Cell, null);
            CardArtworkLoader.Release(slot.BoundArtworkCard);
            slot.BoundArtworkCard = null;
        }

        /// <summary>Shared by grid cells and the big-card overlay for consistent look. When artwork
        /// is showing, SetChromeForArtwork hides the now-redundant name bar (keeping the "xN" badge).
        /// An unowned card keeps the full placeholder look ("???", dark bars).</summary>
        private static void ApplyCardToCell(UIFactory.CardCellRefs cell, CardData card, bool isOwned, int owned, Sprite artwork)
        {
            cell.Background.color = isOwned
                ? (card.rarity != null ? card.rarity.accentColor : OwnedFallbackColor)
                : UnownedColor;
            cell.NameLabel.text = isOwned ? card.displayName : "???";
            // artwork is passed in already resolved (via CardArtworkLoader.Acquire, or null) rather
            // than read from CardData directly - hidden for unowned cards to keep the mystery.
            UIFactory.SetArtwork(cell, artwork);
            UIFactory.SetChromeForArtwork(cell, artwork != null);
            cell.BadgeLabel.text = isOwned && owned > 1 ? $"x{owned}" : "";
        }

        // --- Tap a card cell to see it big on screen (same idea as BoosterOpenScreen's grid review) ---

        private void ShowBigCard(CardData card, bool isOwned, int owned)
        {
            BuildBigCardOverlayIfNeeded();

            // The overlay cell is reused, never destroyed, so release its previous artwork
            // explicitly before binding the new card.
            ReleaseBigCardArtwork();

            Sprite artwork = null;
            if (isOwned)
            {
                artwork = CardArtworkLoader.Acquire(card);
                if (artwork != null) _bigCardArtworkBound = card;
            }

            ApplyCardToCell(_bigCell, card, isOwned, owned, artwork);
            _bigCardOverlay.SetActive(true);
            _bigCell.Root.transform.localScale = Vector3.zero;
            StartCoroutine(PopIn(_bigCell.Root.transform));
        }

        private void HideBigCard()
        {
            if (_bigCardOverlay != null) _bigCardOverlay.SetActive(false);
            ReleaseBigCardArtwork();
            CardArtworkLoader.RequestSweep();
        }

        /// <summary>Clears the big cell's Image reference before releasing the card it was bound
        /// to - the overlay is reused rather than destroyed.</summary>
        private void ReleaseBigCardArtwork()
        {
            if (_bigCardArtworkBound == null) return;
            UIFactory.SetArtwork(_bigCell, null);
            CardArtworkLoader.Release(_bigCardArtworkBound);
            _bigCardArtworkBound = null;
        }

        private void BuildBigCardOverlayIfNeeded()
        {
            if (_bigCardOverlay != null) return;

            // Parented to the Canvas root (not this panel) so it covers the whole screen, including
            // the bottom nav bar.
            var canvasRoot = transform.root;

            var overlayGO = new GameObject("BinderBigCardOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
            var overlayRt = (RectTransform)overlayGO.transform;
            overlayRt.SetParent(canvasRoot, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            overlayGO.GetComponent<Image>().color = BigCardOverlayBackground;
            overlayGO.transform.SetAsLastSibling(); // always drawn on top of every screen/nav bar

            // Tapping anywhere on the dark backdrop closes it, not just the card itself.
            overlayGO.GetComponent<Button>().onClick.AddListener(HideBigCard);

            _bigCell = UIFactory.CreateCardCell(overlayRt, BigCellSize);
            var bigRt = (RectTransform)_bigCell.Root.transform;
            bigRt.anchorMin = bigRt.anchorMax = new Vector2(0.5f, 0.5f);
            bigRt.pivot = new Vector2(0.5f, 0.5f);
            bigRt.anchoredPosition = Vector2.zero;
            _bigCell.RootButton.onClick.AddListener(HideBigCard);

            _bigCardOverlay = overlayGO;
            _bigCardOverlay.SetActive(false);
        }

        private static IEnumerator PopIn(Transform target, float duration = 0.25f)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                target.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, t);
                yield return null;
            }
            target.localScale = Vector3.one;
        }
    }
}