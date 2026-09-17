using System.Collections;
using System.Collections.Generic;
using TCGCollector.Core;
using TCGCollector.Data;
using TCGCollector.Systems;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.UI
{
    /// <summary>
    /// Boutique + Marketplace + Rachat: three tabs sharing one screen. "Boutique du jour" is the
    /// one-offer-per-Licence daily rotation (see ShopManager). "Marketplace" is real
    /// player-to-player trading via MarketplaceService (Cloud Code, server-authoritative).
    /// "Rachat" is a low-price, instant, purely-local buyback for excess duplicates (see
    /// CollectionManager.TrySellToSystem/GetSystemBuybackPrice) - cheap/fast versus the
    /// Marketplace's real-value, player-negotiated selling. All three tabs reuse the same
    /// card-grid area (ShopList/Viewport/Content); only what Refresh() populates it with changes.
    ///
    /// Expects this child hierarchy (built by UISceneBuilder.AddShopScreen):
    ///   Header (Text)
    ///   ShopList (via UIFactory.CreateVerticalScrollList -> "ShopList/Viewport/Content" -
    ///             EnsureGridLayout retrofits its Content into a GridLayoutGroup at runtime)
    /// TabRow and the "Vendre une carte" button are built entirely at runtime in Awake, shrinking
    /// ShopList's band to make room.
    /// </summary>
    public class ShopScreen : MonoBehaviour
    {
        private enum Tab { DailyShop, Marketplace, Buyback }

        private const int Columns = 2;
        private static readonly Vector2 CellSize = new Vector2(320, 480);
        private static readonly Vector2 CellSpacing = new Vector2(20, 20);
        private static readonly Vector2 CardArtSize = new Vector2(280, 320); // placeholder, overridden to stretch-fill its band
        private static readonly Vector2 BigCellSize = new Vector2(700, 860);
        private static readonly Color RowBackground = new Color(0.18f, 0.18f, 0.22f);
        private static readonly Color BuyButtonColor = new Color(0.35f, 0.55f, 0.9f);
        private static readonly Color CancelButtonColor = new Color(0.75f, 0.35f, 0.3f);
        private static readonly Color DisabledButtonColor = new Color(0.3f, 0.3f, 0.3f);
        private static readonly Color BigCardOverlayBackground = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color TabSelectedColor = new Color(0.35f, 0.55f, 0.9f);
        private static readonly Color TabDefaultColor = new Color(0.22f, 0.22f, 0.26f);
        private static readonly Color SellButtonColor = new Color(0.35f, 0.65f, 0.4f);

        private Text _header;
        private Transform _listContent;
        private Tab _selectedTab = Tab.DailyShop;

        private Button _tabDailyButton;
        private Button _tabMarketplaceButton;
        private Button _tabBuybackButton;
        private Button _sellButton;
        private Button _buybackAllButton;
        private Text _statusText;

        // Cards whose artwork is currently Acquire()'d by a live grid cell - released and cleared
        // at the start of every Refresh(), before the old cells are destroyed. Separate from the
        // big-card overlay's own artwork, which persists across refreshes.
        private readonly List<CardData> _acquiredCards = new List<CardData>();

        // --- Tap a card cell to see it big on screen (same idea as BinderScreen's grid review) ---
        private GameObject _bigCardOverlay;
        private UIFactory.CardCellRefs _bigCell;
        private CardData _bigCardArtworkBound;

        // --- "Vendre une carte" overlay ---
        private GameObject _sellOverlay;
        private Transform _sellListContent;

        // Bumped every time a marketplace async call starts, so a call that finishes after a
        // newer one (or after the tab changed) doesn't clobber the screen with stale data.
        private int _requestToken;

        private void Awake()
        {
            _header = transform.Find("Header").GetComponent<Text>();
            _listContent = transform.Find("ShopList/Viewport/Content");
            EnsureGridLayout();
            BuildTabRow();
        }

        /// <summary>Retrofits ShopList/Viewport/Content into a fixed-size N-column grid at runtime,
        /// replacing the single full-width column UIFactory.CreateVerticalScrollList originally
        /// built. Safe to call every time - skips re-adding a GridLayoutGroup that's already there.</summary>
        private void EnsureGridLayout()
        {
            var contentGO = _listContent.gameObject;

            // DestroyImmediate, not Destroy: Destroy() defers to end of frame, and LayoutGroup
            // enforces DisallowMultipleComponent, so the old VerticalLayoutGroup would still be
            // present when AddComponent<GridLayoutGroup> runs below, causing it to silently fail
            // and NullReferenceException on the next line. Skip if a GridLayoutGroup already exists.
            foreach (var lg in contentGO.GetComponents<LayoutGroup>())
            {
                if (lg is GridLayoutGroup) continue;
                DestroyImmediate(lg);
            }

            var grid = contentGO.GetComponent<GridLayoutGroup>();
            if (grid == null) grid = contentGO.AddComponent<GridLayoutGroup>();

            grid.cellSize = CellSize;
            grid.spacing = CellSpacing;
            grid.padding = new RectOffset(20, 20, 20, 20);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Columns;
            // Centers rows horizontally instead of the default upper-left packing, which left a
            // gap on the right when Content's width doesn't exactly match Columns * cell size.
            grid.childAlignment = TextAnchor.UpperCenter;
        }

        /// <summary>Builds the tab row (Boutique du jour / Marketplace / Rachat) plus a status
        /// line, squeezed into a thin band taken off the TOP of ShopList's current anchors
        /// (whatever UISceneBuilder actually baked, not assumed numbers).</summary>
        private void BuildTabRow()
        {
            var shopListRt = (RectTransform)transform.Find("ShopList");
            float bandTop = shopListRt.anchorMax.y;
            float bandBottom = shopListRt.anchorMin.y;
            float bandHeight = bandTop - bandBottom;

            // Reserve the top ~16% of ShopList's original band for the tab row + status line,
            // shrink ShopList itself to start right below that.
            float reserved = bandHeight * 0.16f;
            float tabRowTop = bandTop;
            float tabRowBottom = bandTop - reserved;
            shopListRt.anchorMax = new Vector2(shopListRt.anchorMax.x, tabRowBottom);

            var rowGO = new GameObject("TabRow", typeof(RectTransform));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(transform, false);
            rowRt.anchorMin = new Vector2(0f, tabRowBottom);
            rowRt.anchorMax = new Vector2(1f, tabRowTop);
            rowRt.offsetMin = new Vector2(20, 4);
            rowRt.offsetMax = new Vector2(-20, -4);

            // Three tab buttons side by side in the top ~60% of the reserved band, a status line
            // underneath.
            _tabDailyButton = UIFactory.CreateButton(rowRt, "Boutique du jour", TabDefaultColor, Color.white, () => SelectTab(Tab.DailyShop));
            var dailyRt = (RectTransform)_tabDailyButton.transform;
            dailyRt.anchorMin = new Vector2(0f, 0.4f);
            dailyRt.anchorMax = new Vector2(0.32f, 1f);
            dailyRt.offsetMin = Vector2.zero;
            dailyRt.offsetMax = Vector2.zero;

            _tabMarketplaceButton = UIFactory.CreateButton(rowRt, "Marketplace", TabDefaultColor, Color.white, () => SelectTab(Tab.Marketplace));
            var marketRt = (RectTransform)_tabMarketplaceButton.transform;
            marketRt.anchorMin = new Vector2(0.34f, 0.4f);
            marketRt.anchorMax = new Vector2(0.66f, 1f);
            marketRt.offsetMin = Vector2.zero;
            marketRt.offsetMax = Vector2.zero;

            _tabBuybackButton = UIFactory.CreateButton(rowRt, "Rachat", TabDefaultColor, Color.white, () => SelectTab(Tab.Buyback));
            var buybackRt = (RectTransform)_tabBuybackButton.transform;
            buybackRt.anchorMin = new Vector2(0.68f, 0.4f);
            buybackRt.anchorMax = new Vector2(1f, 1f);
            buybackRt.offsetMin = Vector2.zero;
            buybackRt.offsetMax = Vector2.zero;

            var statusGO = new GameObject("StatusText", typeof(RectTransform), typeof(Text));
            var statusRt = (RectTransform)statusGO.transform;
            statusRt.SetParent(rowRt, false);
            statusRt.anchorMin = new Vector2(0f, 0f);
            statusRt.anchorMax = new Vector2(1f, 0.38f);
            statusRt.offsetMin = Vector2.zero;
            statusRt.offsetMax = Vector2.zero;
            _statusText = statusGO.GetComponent<Text>();
            _statusText.font = UIFactory.DefaultFont;
            _statusText.fontSize = 20;
            _statusText.alignment = TextAnchor.MiddleCenter;
            _statusText.color = new Color(1f, 1f, 1f, 0.8f);
            _statusText.text = "";
        }

        private void SelectTab(Tab tab)
        {
            if (_selectedTab == tab) return;
            _selectedTab = tab;
            SetStatus("");
            Refresh();
        }

        private void UpdateTabHighlights()
        {
            if (_tabDailyButton != null)
                _tabDailyButton.GetComponent<Image>().color = _selectedTab == Tab.DailyShop ? TabSelectedColor : TabDefaultColor;
            if (_tabMarketplaceButton != null)
                _tabMarketplaceButton.GetComponent<Image>().color = _selectedTab == Tab.Marketplace ? TabSelectedColor : TabDefaultColor;
            if (_tabBuybackButton != null)
                _tabBuybackButton.GetComponent<Image>().color = _selectedTab == Tab.Buyback ? TabSelectedColor : TabDefaultColor;
        }

        private void SetStatus(string message)
        {
            if (_statusText != null) _statusText.text = message;
        }

        private void OnEnable()
        {
            StartCoroutine(WaitForGameManagerThenRefresh());
        }

        private IEnumerator WaitForGameManagerThenRefresh()
        {
            while (GameManager.Instance == null || GameManager.Instance.Shop == null)
                yield return null;

            Refresh();
        }

        public void Refresh()
        {
            if (GameManager.Instance == null || GameManager.Instance.Shop == null) return;

            UpdateTabHighlights();
            ClearGrid();

            // RefreshMarketplaceAsync/RefreshBuyback also toggle these via their Ensure* methods,
            // but doing it here too covers DailyShop, which never calls either and would otherwise
            // leave a button visible from an earlier tab visit.
            if (_sellButton != null)
                _sellButton.gameObject.SetActive(_selectedTab == Tab.Marketplace);
            if (_buybackAllButton != null)
                _buybackAllButton.gameObject.SetActive(_selectedTab == Tab.Buyback);

            switch (_selectedTab)
            {
                case Tab.DailyShop:
                    RefreshDailyShop();
                    break;
                case Tab.Marketplace:
                    RefreshMarketplaceAsync();
                    break;
                case Tab.Buyback:
                    RefreshBuyback();
                    break;
            }
        }

        private void ClearGrid()
        {
            foreach (var card in _acquiredCards)
                CardArtworkLoader.Release(card);
            _acquiredCards.Clear();

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
        }

        // ============================= Boutique du jour (unchanged) =============================

        private void RefreshDailyShop()
        {
            var offers = GameManager.Instance.Shop.GetTodaysOffers();
            foreach (var offer in offers)
                BuildDailyCell(offer);

            CardArtworkLoader.RequestSweep();

            _header.text = $"Boutique - {GameManager.Instance.Collection.Currency} po";
        }

        private void BuildDailyCell(ShopOffer offer)
        {
            string licenseLabel = offer.License != null ? offer.License.ToString() : "?";

            var cellGO = new GameObject((offer.License != null ? offer.License.licenseId : "unknown") + " Cell", typeof(RectTransform), typeof(Image));
            var cellRt = (RectTransform)cellGO.transform;
            cellRt.SetParent(_listContent, false);
            cellGO.GetComponent<Image>().color = RowBackground;

            if (offer.Card == null)
            {
                // Nothing eligible left to offer for this licence right now - see
                // ShopManager.PickCardForLicense / BackfillEmptyOffers.
                var placeholderRt = UIFactory.CreateStretchRect(cellRt, "Placeholder", typeof(Text));
                var placeholderText = placeholderRt.GetComponent<Text>();
                placeholderText.font = UIFactory.DefaultFont;
                placeholderText.text = $"{licenseLabel}\n\nEn attente de mise a jour";
                placeholderText.fontSize = 24;
                placeholderText.color = new Color(1f, 1f, 1f, 0.6f);
                placeholderText.alignment = TextAnchor.MiddleCenter;
                placeholderText.horizontalOverflow = HorizontalWrapMode.Wrap;
                return;
            }

            // Card art - fills most of the cell, tap to see it big (ShowBigCard) rather than buy
            // directly; buying is a separate button below.
            var cell = UIFactory.CreateCardCell(cellRt, CardArtSize);
            var artRt = (RectTransform)cell.Root.transform;
            artRt.anchorMin = new Vector2(0f, 0.30f);
            artRt.anchorMax = new Vector2(1f, 1f);
            artRt.offsetMin = new Vector2(10f, 0f);
            artRt.offsetMax = new Vector2(-10f, -10f);

            cell.Background.color = offer.Card.rarity != null ? offer.Card.rarity.accentColor : Color.white;
            cell.NameLabel.text = offer.Card.displayName;
            cell.BadgeLabel.text = "";

            _acquiredCards.Add(offer.Card);
            var sprite = CardArtworkLoader.Acquire(offer.Card);
            UIFactory.SetArtwork(cell, sprite);
            UIFactory.SetChromeForArtwork(cell, sprite != null);

            if (offer.AlreadyOwned)
            {
                var canvasGroup = cell.Root.GetComponent<CanvasGroup>();
                if (canvasGroup == null) canvasGroup = cell.Root.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 0.4f;
            }

            CardData capturedCard = offer.Card; // for the closure below
            cell.RootButton.onClick.AddListener(() => ShowBigCard(capturedCard));

            // Licence + rarity (+ owned status), small text under the art.
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(cellRt, false);
            labelRt.anchorMin = new Vector2(0f, 0.14f);
            labelRt.anchorMax = new Vector2(1f, 0.30f);
            labelRt.offsetMin = new Vector2(10f, 0f);
            labelRt.offsetMax = new Vector2(-10f, 0f);

            var label = labelGO.GetComponent<Text>();
            label.font = UIFactory.DefaultFont;
            string rarityName = offer.Card.rarity != null ? offer.Card.rarity.ToString() : "?";
            string statusLine = offer.AlreadyOwned ? " - Deja obtenue" : "";
            label.text = $"{licenseLabel} ({rarityName}){statusLine}";
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            // Buy button.
            int price = offer.Card.rarity != null ? offer.Card.rarity.shopPrice : 0;
            int currency = GameManager.Instance.Collection.Currency;
            bool canBuy = !offer.AlreadyOwned && currency >= price;

            var buttonGO = new GameObject("BuyButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRt = (RectTransform)buttonGO.transform;
            buttonRt.SetParent(cellRt, false);
            buttonRt.anchorMin = new Vector2(0f, 0f);
            buttonRt.anchorMax = new Vector2(1f, 0.14f);
            buttonRt.offsetMin = new Vector2(10f, 6f);
            buttonRt.offsetMax = new Vector2(-10f, -6f);
            buttonGO.GetComponent<Image>().color = offer.AlreadyOwned ? DisabledButtonColor : BuyButtonColor;

            var buttonTextGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var buttonTextRt = (RectTransform)buttonTextGO.transform;
            buttonTextRt.SetParent(buttonRt, false);
            buttonTextRt.anchorMin = Vector2.zero;
            buttonTextRt.anchorMax = Vector2.one;
            buttonTextRt.offsetMin = Vector2.zero;
            buttonTextRt.offsetMax = Vector2.zero;
            var buttonText = buttonTextGO.GetComponent<Text>();
            buttonText.font = UIFactory.DefaultFont;
            buttonText.text = offer.AlreadyOwned ? "Deja obtenue" : $"{price} po";
            buttonText.fontSize = 22;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;

            var buyButton = buttonGO.GetComponent<Button>();
            buyButton.interactable = canBuy;
            License capturedLicense = offer.License; // capture for the closure below
            buyButton.onClick.AddListener(() => OnBuyClicked(capturedLicense));
        }

        private void OnBuyClicked(License license)
        {
            if (GameManager.Instance == null) return;

            if (GameManager.Instance.PurchaseShopOffer(license))
                Refresh();
        }

        // ================================= Marketplace (new) =================================

        private async void RefreshMarketplaceAsync()
        {
            int token = ++_requestToken;
            _header.text = "Marketplace - chargement...";
            EnsureSellButton();

            List<MarketplaceListing> listings;
            try
            {
                listings = await GameManager.Instance.Marketplace.GetListingsAsync();
            }
            catch (MarketplaceException e)
            {
                if (token != _requestToken || this == null) return;
                _header.text = $"Marketplace - {GameManager.Instance.Collection.Currency} po";
                SetStatus($"Erreur: {e.Message}");
                return;
            }

            if (token != _requestToken || this == null) return; // tab changed / screen closed meanwhile

            string myId = AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

            if (listings.Count == 0)
            {
                var placeholderRt = UIFactory.CreateStretchRect(_listContent, "EmptyPlaceholder", typeof(Text));
                var placeholderText = placeholderRt.GetComponent<Text>();
                placeholderText.font = UIFactory.DefaultFont;
                placeholderText.text = "Aucune annonce pour l'instant.\n\nSois le premier a vendre une carte !";
                placeholderText.fontSize = 26;
                placeholderText.color = new Color(1f, 1f, 1f, 0.6f);
                placeholderText.alignment = TextAnchor.MiddleCenter;
                placeholderText.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            else
            {
                foreach (var listing in listings)
                    BuildMarketplaceCell(listing, myId);
            }

            CardArtworkLoader.RequestSweep();
            _header.text = $"Marketplace - {GameManager.Instance.Collection.Currency} po";
        }

        private void BuildMarketplaceCell(MarketplaceListing listing, string myPlayerId)
        {
            var card = GameManager.Instance.CardDatabase.GetById(listing.cardId);

            var cellGO = new GameObject("Listing Cell", typeof(RectTransform), typeof(Image));
            var cellRt = (RectTransform)cellGO.transform;
            cellRt.SetParent(_listContent, false);
            cellGO.GetComponent<Image>().color = RowBackground;

            var cell = UIFactory.CreateCardCell(cellRt, CardArtSize);
            var artRt = (RectTransform)cell.Root.transform;
            artRt.anchorMin = new Vector2(0f, 0.30f);
            artRt.anchorMax = new Vector2(1f, 1f);
            artRt.offsetMin = new Vector2(10f, 0f);
            artRt.offsetMax = new Vector2(-10f, -10f);

            bool isMine = !string.IsNullOrEmpty(myPlayerId) && listing.sellerId == myPlayerId;

            if (card != null)
            {
                cell.Background.color = card.rarity != null ? card.rarity.accentColor : Color.white;
                cell.NameLabel.text = card.displayName;
                _acquiredCards.Add(card);
                var sprite = CardArtworkLoader.Acquire(card);
                UIFactory.SetArtwork(cell, sprite);
                UIFactory.SetChromeForArtwork(cell, sprite != null);

                CardData capturedCard = card;
                cell.RootButton.onClick.AddListener(() => ShowBigCard(capturedCard));
            }
            else
            {
                cell.Background.color = Color.white;
                cell.NameLabel.text = "?";
            }
            cell.BadgeLabel.text = "";

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(cellRt, false);
            labelRt.anchorMin = new Vector2(0f, 0.14f);
            labelRt.anchorMax = new Vector2(1f, 0.30f);
            labelRt.offsetMin = new Vector2(10f, 0f);
            labelRt.offsetMax = new Vector2(-10f, 0f);

            var label = labelGO.GetComponent<Text>();
            label.font = UIFactory.DefaultFont;
            label.text = isMine ? "Ton annonce" : "En vente";
            label.fontSize = 20;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            var buttonGO = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRt = (RectTransform)buttonGO.transform;
            buttonRt.SetParent(cellRt, false);
            buttonRt.anchorMin = new Vector2(0f, 0f);
            buttonRt.anchorMax = new Vector2(1f, 0.14f);
            buttonRt.offsetMin = new Vector2(10f, 6f);
            buttonRt.offsetMax = new Vector2(-10f, -6f);

            var buttonTextGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var buttonTextRt = (RectTransform)buttonTextGO.transform;
            buttonTextRt.SetParent(buttonRt, false);
            buttonTextRt.anchorMin = Vector2.zero;
            buttonTextRt.anchorMax = Vector2.one;
            buttonTextRt.offsetMin = Vector2.zero;
            buttonTextRt.offsetMax = Vector2.zero;
            var buttonText = buttonTextGO.GetComponent<Text>();
            buttonText.font = UIFactory.DefaultFont;
            buttonText.fontSize = 22;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;

            var actionButton = buttonGO.GetComponent<Button>();
            string listingId = listing.listingId;

            if (isMine)
            {
                buttonGO.GetComponent<Image>().color = CancelButtonColor;
                buttonText.text = $"Annuler ({listing.price} po)";
                actionButton.onClick.AddListener(() => OnCancelListingClicked(listingId));
            }
            else
            {
                int currency = GameManager.Instance.Collection.Currency;
                bool canBuy = currency >= listing.price;
                buttonGO.GetComponent<Image>().color = canBuy ? BuyButtonColor : DisabledButtonColor;
                buttonText.text = $"{listing.price} po";
                actionButton.interactable = canBuy;
                actionButton.onClick.AddListener(() => OnBuyListingClicked(listingId));
            }
        }

        private async void OnBuyListingClicked(string listingId)
        {
            SetStatus("Achat en cours...");
            try
            {
                await GameManager.Instance.Marketplace.BuyListingAsync(listingId);
                if (this == null) return;
                SetStatus("Carte achetee !");
                Refresh();
            }
            catch (MarketplaceException e)
            {
                if (this == null) return;
                SetStatus($"Erreur: {e.Message}");
            }
        }

        private async void OnCancelListingClicked(string listingId)
        {
            SetStatus("Annulation en cours...");
            try
            {
                await GameManager.Instance.Marketplace.CancelListingAsync(listingId);
                if (this == null) return;
                SetStatus("Annonce annulee, carte recuperee.");
                Refresh();
            }
            catch (MarketplaceException e)
            {
                if (this == null) return;
                SetStatus($"Erreur: {e.Message}");
            }
        }

        // ================================= Rachat systeme =================================
        // Rachat bas prix, instantane, purement local (pas de Cloud Code) pour les doublons en
        // trop. Ne liste que les cartes possedees en plus d'un exemplaire, et ne vend jamais le
        // dernier exemplaire (voir CollectionManager.TrySellToSystem).

        private void RefreshBuyback()
        {
            EnsureBuybackAllButton();

            var entries = GameManager.Instance.Collection.GetOwnedCardEntries();
            entries.RemoveAll(e => e.count <= 1);
            entries.Sort((a, b) => b.count.CompareTo(a.count)); // les plus gros tas de doublons en premier

            if (entries.Count == 0)
            {
                var placeholderRt = UIFactory.CreateStretchRect(_listContent, "EmptyPlaceholder", typeof(Text));
                var placeholderText = placeholderRt.GetComponent<Text>();
                placeholderText.font = UIFactory.DefaultFont;
                placeholderText.text = "Aucun doublon a racheter pour l'instant.";
                placeholderText.fontSize = 26;
                placeholderText.color = new Color(1f, 1f, 1f, 0.6f);
                placeholderText.alignment = TextAnchor.MiddleCenter;
                placeholderText.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            else
            {
                foreach (var (cardId, count) in entries)
                    BuildBuybackCell(cardId, count);
            }

            CardArtworkLoader.RequestSweep();
            _header.text = $"Rachat - {GameManager.Instance.Collection.Currency} po";
        }

        private void BuildBuybackCell(string cardId, int count)
        {
            var card = GameManager.Instance.CardDatabase.GetById(cardId);
            if (card == null) return; // id perime/carte retiree de la base - on saute plutot que de planter

            int unitPrice = CollectionManager.GetSystemBuybackPrice(card);
            int maxSellable = Mathf.Max(0, count - 1); // toujours garder au moins un exemplaire

            var cellGO = new GameObject("Buyback Cell", typeof(RectTransform), typeof(Image));
            var cellRt = (RectTransform)cellGO.transform;
            cellRt.SetParent(_listContent, false);
            cellGO.GetComponent<Image>().color = RowBackground;

            var cell = UIFactory.CreateCardCell(cellRt, CardArtSize);
            var artRt = (RectTransform)cell.Root.transform;
            artRt.anchorMin = new Vector2(0f, 0.46f);
            artRt.anchorMax = new Vector2(1f, 1f);
            artRt.offsetMin = new Vector2(10f, 0f);
            artRt.offsetMax = new Vector2(-10f, -10f);

            cell.Background.color = card.rarity != null ? card.rarity.accentColor : Color.white;
            cell.NameLabel.text = card.displayName;
            cell.BadgeLabel.text = $"x{count}";

            _acquiredCards.Add(card);
            var sprite = CardArtworkLoader.Acquire(card);
            UIFactory.SetArtwork(cell, sprite);
            UIFactory.SetChromeForArtwork(cell, sprite != null);

            CardData capturedCard = card;
            cell.RootButton.onClick.AddListener(() => ShowBigCard(capturedCard));

            // Rarete + prix unitaire de rachat, sous l'art.
            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(cellRt, false);
            labelRt.anchorMin = new Vector2(0f, 0.32f);
            labelRt.anchorMax = new Vector2(1f, 0.46f);
            labelRt.offsetMin = new Vector2(10f, 0f);
            labelRt.offsetMax = new Vector2(-10f, 0f);

            var label = labelGO.GetComponent<Text>();
            label.font = UIFactory.DefaultFont;
            string rarityName = card.rarity != null ? card.rarity.ToString() : "?";
            label.text = $"{rarityName} - {unitPrice} po/carte";
            label.fontSize = 18;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            // Quantite a vendre - pre-remplie a "tous les doublons sauf le dernier", modifiable.
            var qtyField = UIFactory.CreateInputField(cellRt, "Qte");
            qtyField.contentType = InputField.ContentType.IntegerNumber;
            qtyField.text = maxSellable.ToString();
            var qtyRt = (RectTransform)qtyField.transform;
            qtyRt.anchorMin = new Vector2(0.06f, 0.17f);
            qtyRt.anchorMax = new Vector2(0.4f, 0.31f);
            qtyRt.offsetMin = Vector2.zero;
            qtyRt.offsetMax = Vector2.zero;

            var maxLabelGO = new GameObject("MaxLabel", typeof(RectTransform), typeof(Text));
            var maxLabelRt = (RectTransform)maxLabelGO.transform;
            maxLabelRt.SetParent(cellRt, false);
            maxLabelRt.anchorMin = new Vector2(0.42f, 0.17f);
            maxLabelRt.anchorMax = new Vector2(0.94f, 0.31f);
            maxLabelRt.offsetMin = Vector2.zero;
            maxLabelRt.offsetMax = Vector2.zero;
            var maxLabelText = maxLabelGO.GetComponent<Text>();
            maxLabelText.font = UIFactory.DefaultFont;
            maxLabelText.text = $"/ {maxSellable} max";
            maxLabelText.fontSize = 16;
            maxLabelText.color = new Color(1f, 1f, 1f, 0.6f);
            maxLabelText.alignment = TextAnchor.MiddleLeft;

            var sellButton = UIFactory.CreateButton(cellRt, $"Vendre ({unitPrice * maxSellable} po)", SellButtonColor, Color.white, null);
            var sellButtonRt = (RectTransform)sellButton.transform;
            sellButtonRt.anchorMin = new Vector2(0.06f, 0.02f);
            sellButtonRt.anchorMax = new Vector2(0.94f, 0.15f);
            sellButtonRt.offsetMin = Vector2.zero;
            sellButtonRt.offsetMax = Vector2.zero;

            string capturedCardId = cardId;
            sellButton.onClick.AddListener(() => OnBuybackRowSellClicked(capturedCardId, qtyField));
        }

        private void OnBuybackRowSellClicked(string cardId, InputField qtyField)
        {
            if (!int.TryParse(qtyField.text, out int qty) || qty <= 0)
            {
                SetStatus("Quantite invalide.");
                return;
            }

            if (!GameManager.Instance.Collection.TrySellToSystem(cardId, qty, out int totalEarned))
            {
                SetStatus("Impossible de vendre cette quantite (verifie combien tu en as encore).");
                return;
            }

            GameManager.Instance.Collection.Save();
            SetStatus($"{qty} carte(s) vendue(s) pour {totalEarned} po.");
            Refresh();
        }

        /// <summary>Vend d'un coup tous les doublons de toutes les cartes (garde toujours un
        /// exemplaire de chacune). Pas de confirmation avant, juste un message de statut apres,
        /// pour rester rapide.</summary>
        private void OnBuybackAllClicked()
        {
            var entries = GameManager.Instance.Collection.GetOwnedCardEntries();
            entries.RemoveAll(e => e.count <= 1);

            if (entries.Count == 0)
            {
                SetStatus("Aucun doublon a vendre.");
                return;
            }

            int totalEarned = 0;
            int totalCards = 0;
            foreach (var (cardId, count) in entries)
            {
                int quantity = count - 1;
                if (GameManager.Instance.Collection.TrySellToSystem(cardId, quantity, out int earned))
                {
                    totalEarned += earned;
                    totalCards += quantity;
                }
            }

            GameManager.Instance.Collection.Save();
            SetStatus($"{totalCards} carte(s) vendue(s) pour {totalEarned} po au total.");
            Refresh();
        }

        /// <summary>Creates the floating "Tout vendre les doublons" button once (persists across
        /// tab switches), shown only while Rachat is selected. Shares the same reserved strip
        /// below TabRow as EnsureSellButton's button - only one is ever visible at a time, so
        /// whichever Ensure* runs first shrinks ShopList's band and the other is a no-op.</summary>
        private void EnsureBuybackAllButton()
        {
            if (_buybackAllButton == null)
            {
                var tabRowRt = (RectTransform)transform.Find("TabRow");
                _buybackAllButton = UIFactory.CreateButton(transform, "Tout vendre les doublons", SellButtonColor, Color.white, OnBuybackAllClicked);
                var rt = (RectTransform)_buybackAllButton.transform;
                float tabBottom = tabRowRt.anchorMin.y;
                float stripHeight = (tabRowRt.anchorMax.y - tabRowRt.anchorMin.y) * 0.55f;
                rt.anchorMin = new Vector2(0.25f, tabBottom - stripHeight);
                rt.anchorMax = new Vector2(0.75f, tabBottom - 0.01f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _buybackAllButton.transform.SetAsLastSibling();

                var shopListRt = (RectTransform)transform.Find("ShopList");
                shopListRt.anchorMax = new Vector2(shopListRt.anchorMax.x, tabBottom - stripHeight - 0.01f);
            }

            _buybackAllButton.gameObject.SetActive(_selectedTab == Tab.Buyback);
        }

        // --- "Vendre une carte" button + overlay ---

        /// <summary>Creates the floating "Vendre une carte" button once (persists across tab
        /// switches), shown only while Marketplace is selected.</summary>
        private void EnsureSellButton()
        {
            if (_sellButton == null)
            {
                var tabRowRt = (RectTransform)transform.Find("TabRow");
                _sellButton = UIFactory.CreateButton(transform, "+ Vendre une carte", SellButtonColor, Color.white, OnSellButtonClicked);
                var rt = (RectTransform)_sellButton.transform;
                // A thin strip just below TabRow's band, anchored relative to TabRow's own
                // already-computed band rather than redoing that math here.
                float tabBottom = tabRowRt.anchorMin.y;
                float stripHeight = (tabRowRt.anchorMax.y - tabRowRt.anchorMin.y) * 0.55f;
                rt.anchorMin = new Vector2(0.25f, tabBottom - stripHeight);
                rt.anchorMax = new Vector2(0.75f, tabBottom - 0.01f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _sellButton.transform.SetAsLastSibling();

                // Shrink ShopList a little further to make room for this strip too.
                var shopListRt = (RectTransform)transform.Find("ShopList");
                shopListRt.anchorMax = new Vector2(shopListRt.anchorMax.x, tabBottom - stripHeight - 0.01f);
            }

            _sellButton.gameObject.SetActive(_selectedTab == Tab.Marketplace);
        }

        private void OnSellButtonClicked()
        {
            BuildSellOverlayIfNeeded();
            RefreshSellOverlay();
            _sellOverlay.SetActive(true);
        }

        private void BuildSellOverlayIfNeeded()
        {
            if (_sellOverlay != null) return;

            var canvasRoot = transform.root;

            var overlayGO = new GameObject("SellOverlay", typeof(RectTransform), typeof(Image));
            var overlayRt = (RectTransform)overlayGO.transform;
            overlayRt.SetParent(canvasRoot, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            overlayGO.GetComponent<Image>().color = BigCardOverlayBackground;
            overlayGO.transform.SetAsLastSibling();

            var panelGO = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var panelRt = (RectTransform)panelGO.transform;
            panelRt.SetParent(overlayRt, false);
            panelRt.anchorMin = new Vector2(0.08f, 0.1f);
            panelRt.anchorMax = new Vector2(0.92f, 0.9f);
            panelRt.offsetMin = Vector2.zero;
            panelRt.offsetMax = Vector2.zero;
            panelGO.GetComponent<Image>().color = new Color(0.14f, 0.14f, 0.17f);

            var titleGO = new GameObject("Title", typeof(RectTransform), typeof(Text));
            var titleRt = (RectTransform)titleGO.transform;
            titleRt.SetParent(panelRt, false);
            titleRt.anchorMin = new Vector2(0f, 0.92f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;
            var titleText = titleGO.GetComponent<Text>();
            titleText.font = UIFactory.DefaultFont;
            titleText.text = "Vendre une carte";
            titleText.fontSize = 30;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;

            var closeButton = UIFactory.CreateButton(panelRt, "Fermer", DisabledButtonColor, Color.white, () => _sellOverlay.SetActive(false));
            var closeRt = (RectTransform)closeButton.transform;
            closeRt.anchorMin = new Vector2(0.75f, 0.92f);
            closeRt.anchorMax = new Vector2(0.98f, 1f);
            closeRt.offsetMin = Vector2.zero;
            closeRt.offsetMax = Vector2.zero;

            var listRt = UIFactory.CreateVerticalScrollList(panelRt, "OwnedList", 10f);
            var scrollRootRt = (RectTransform)listRt.parent.parent; // ScrollRect root created by CreateVerticalScrollList (listRt/Viewport/scrollRoot)
            scrollRootRt.anchorMin = new Vector2(0.03f, 0.02f);
            scrollRootRt.anchorMax = new Vector2(0.97f, 0.90f);
            scrollRootRt.offsetMin = Vector2.zero;
            scrollRootRt.offsetMax = Vector2.zero;
            _sellListContent = listRt;

            _sellOverlay = overlayGO;
            _sellOverlay.SetActive(false);
        }

        /// <summary>Rebuilds the owned-card list inside the sell overlay from scratch every time
        /// it's opened - cheap, and avoids showing a stale count from before a purchase/sale.</summary>
        private void RefreshSellOverlay()
        {
            for (int i = _sellListContent.childCount - 1; i >= 0; i--)
                Destroy(_sellListContent.GetChild(i).gameObject);

            var entries = GameManager.Instance.Collection.GetOwnedCardEntries();
            entries.Sort((a, b) => string.Compare(a.cardId, b.cardId, System.StringComparison.Ordinal));

            if (entries.Count == 0)
            {
                var rowGO = new GameObject("EmptyRow", typeof(RectTransform), typeof(Text));
                UIFactory.SetPreferredSize(rowGO, 0, 60);
                rowGO.transform.SetParent(_sellListContent, false);
                var text = rowGO.GetComponent<Text>();
                text.font = UIFactory.DefaultFont;
                text.text = "Tu ne possedes aucune carte a vendre.";
                text.fontSize = 22;
                text.color = new Color(1f, 1f, 1f, 0.6f);
                text.alignment = TextAnchor.MiddleCenter;
                return;
            }

            foreach (var (cardId, count) in entries)
                BuildSellRow(cardId, count);
        }

        private void BuildSellRow(string cardId, int count)
        {
            var card = GameManager.Instance.CardDatabase.GetById(cardId);
            string name = card != null ? card.displayName : cardId;
            int defaultPrice = card != null && card.rarity != null ? Mathf.Max(1, card.rarity.sellValue * 3) : 100;

            var rowGO = new GameObject("Row", typeof(RectTransform), typeof(Image));
            UIFactory.SetPreferredSize(rowGO, 0, 70);
            rowGO.transform.SetParent(_sellListContent, false);
            rowGO.GetComponent<Image>().color = RowBackground;
            var rowRt = (RectTransform)rowGO.transform;

            var nameGO = new GameObject("Name", typeof(RectTransform), typeof(Text));
            var nameRt = (RectTransform)nameGO.transform;
            nameRt.SetParent(rowRt, false);
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(0.5f, 1f);
            nameRt.offsetMin = new Vector2(10, 0);
            nameRt.offsetMax = Vector2.zero;
            var nameText = nameGO.GetComponent<Text>();
            nameText.font = UIFactory.DefaultFont;
            nameText.text = $"{name} (x{count})";
            nameText.fontSize = 22;
            nameText.color = Color.white;
            nameText.alignment = TextAnchor.MiddleLeft;
            nameText.horizontalOverflow = HorizontalWrapMode.Wrap;

            var priceField = UIFactory.CreateInputField(rowRt, "Prix");
            priceField.contentType = InputField.ContentType.IntegerNumber;
            priceField.text = defaultPrice.ToString();
            var priceRt = (RectTransform)priceField.transform;
            priceRt.anchorMin = new Vector2(0.5f, 0.15f);
            priceRt.anchorMax = new Vector2(0.72f, 0.85f);
            priceRt.offsetMin = Vector2.zero;
            priceRt.offsetMax = Vector2.zero;

            var sellButton = UIFactory.CreateButton(rowRt, "Vendre", SellButtonColor, Color.white, null);
            var sellRt = (RectTransform)sellButton.transform;
            sellRt.anchorMin = new Vector2(0.74f, 0.1f);
            sellRt.anchorMax = new Vector2(0.98f, 0.9f);
            sellRt.offsetMin = Vector2.zero;
            sellRt.offsetMax = Vector2.zero;

            string capturedCardId = cardId;
            sellButton.onClick.AddListener(() => OnSellRowConfirmClicked(capturedCardId, priceField, sellButton));
        }

        private async void OnSellRowConfirmClicked(string cardId, InputField priceField, Button sellButton)
        {
            if (!int.TryParse(priceField.text, out int price) || price <= 0)
            {
                SetStatus("Prix invalide.");
                return;
            }

            sellButton.interactable = false;
            SetStatus("Mise en vente...");
            try
            {
                await GameManager.Instance.Marketplace.ListCardAsync(cardId, price);
                if (this == null) return;
                _sellOverlay.SetActive(false);
                SetStatus("Carte mise en vente !");
                Refresh();
            }
            catch (MarketplaceException e)
            {
                if (this == null) return;
                sellButton.interactable = true;
                SetStatus($"Erreur: {e.Message}");
            }
        }

        // --- Tap a card cell to see it big on screen (same idea as BinderScreen's grid review) ---

        private void ShowBigCard(CardData card)
        {
            BuildBigCardOverlayIfNeeded();

            // The overlay cell is reused, never destroyed, so release its previous artwork
            // explicitly before binding the new card.
            ReleaseBigCardArtwork();

            var artwork = CardArtworkLoader.Acquire(card);
            if (artwork != null) _bigCardArtworkBound = card;

            _bigCell.Background.color = card.rarity != null ? card.rarity.accentColor : Color.white;
            _bigCell.NameLabel.text = card.displayName;
            _bigCell.BadgeLabel.text = "";
            UIFactory.SetArtwork(_bigCell, artwork);
            UIFactory.SetChromeForArtwork(_bigCell, artwork != null);

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

            var overlayGO = new GameObject("ShopBigCardOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
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
