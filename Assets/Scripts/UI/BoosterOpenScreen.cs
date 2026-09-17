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
    /// Booster-opening screen: pick a License, then a Set, then open a pack and reveal cards
    /// one at a time (tap to advance, TCG-Pocket style). After the last card it switches to a
    /// summary grid - tap a card there to bring it back up big.
    ///
    /// License -> Set is a two-tier cascading picker (see SelectLicense): picking a License
    /// rebuilds the Set row to that License's sets and auto-selects the first one.
    ///
    /// Vibrates on reveal when the card's Rarity has vibrateOnPull checked (see
    /// CardRarity.vibrateOnPull), toggleable globally via VibrationSettings.Enabled.
    ///
    /// Buying a Display asks the player how to open it (see ShowDisplayChoiceOverlay):
    /// "Tout ouvrir d'un coup" reveals everything now (RunDisplayPurchaseOpenAll), "Ouvrir une
    /// par une" banks the boosters unopened (GameManager.PurchaseDisplayBanked) to be drained
    /// one at a time via OnOpenBoosterClicked / _bankedCountLabel.
    ///
    /// Expects this child hierarchy (built by UISceneBuilder):
    ///   Title (Text)
    ///   SetButtonsRow (HorizontalLayoutGroup) - shifted down at runtime to make room for a
    ///     new LicenseButtonsRow above it (see BuildLicenseButtonsRow).
    ///   OpenButton (Button)
    ///   PurchaseRow (HorizontalLayoutGroup - Buy Booster/Bundle/Display buttons built at runtime)
    ///   BigCardSlot (the one-at-a-time/review card is built into this at runtime; re-anchored
    ///     to fill the space freed by SetTopControlsVisible(false))
    ///   RevealArea (built via UIFactory.CreateVerticalScrollGrid -> "RevealArea/Viewport/Content")
    ///   ResultText (Text)
    /// </summary>
    public class BoosterOpenScreen : MonoBehaviour
    {
        [Header("Debug")]
        [Tooltip("Debug: ignore the once-per-day free booster limit for testing. Turn off before shipping.")]
        [SerializeField] private bool debugIgnoreDailyLimit = true;

        private static readonly Vector2 GridCellSize = new Vector2(190, 260);

        // Real trading-card ratio (2.5in x 3.5in), sized to fill the enlarged BigCardSlot band (see Awake()).
        private static readonly Vector2 BigCellSize = new Vector2(900, 1260);
        private static readonly Color SetButtonDefaultColor = new Color(0.25f, 0.25f, 0.3f);
        private static readonly Color SetButtonSelectedColor = new Color(0.35f, 0.55f, 0.9f);
        private static readonly Color PurchaseButtonColor = new Color(0.55f, 0.4f, 0.15f);
        private static readonly Color OverlayBackground = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color BoxBackground = new Color(0.14f, 0.14f, 0.18f);
        private static readonly Color CancelButtonColor = new Color(0.3f, 0.3f, 0.34f);

        private Text _title;
        private Transform _licenseButtonsRow;
        private Transform _setButtonsRow;
        private Button _openButton;
        private Text _bankedCountLabel;
        private GameObject _fastOpenToggleRoot;
        private Button _fastOpenToggleButton;
        private Text _fastOpenToggleCheckmark;
        private Transform _purchaseRow;
        private Button _buyBoosterButton;
        private Button _buyBundleButton;
        private Button _buyDisplayButton;
        private GameObject _bigCardSlot;
        private UIFactory.CardCellRefs _bigCell;
        private GameObject _revealAreaRoot;
        private Transform _revealContent;
        private Text _resultText;

        private GameObject _displayChoiceOverlay;
        private Text _displayChoiceMessageText;

        private License _selectedLicense;
        private CardSet _selectedSet;
        private string _selectedSetLabel;
        private bool _buttonsBuilt;
        private bool _purchaseButtonsBuilt;
        private readonly List<(License license, Button button)> _licenseButtons = new List<(License, Button)>();
        private readonly List<(CardSet set, Button button)> _setButtons = new List<(CardSet, Button)>();

        private List<CollectionManager.CardPullResult> _currentResults;
        // Subset of _currentResults shown one-at-a-time (see BuildRevealQueue). _revealIndex
        // indexes into this list, not _currentResults (ShowGrid still iterates the full list).
        private List<CollectionManager.CardPullResult> _revealQueue = new List<CollectionManager.CardPullResult>();
        private int _revealIndex;
        private bool _isReviewingFromGrid;

        // Tracks which cards hold an artwork Acquire() from the results grid, and which card
        // (if any) the reused big cell holds one for, so CardArtworkLoader references always
        // get released (see CardArtworkLoader).
        private readonly List<CardData> _revealBoundCards = new List<CardData>();
        private CardData _bigCardArtworkBound;

        // --- 2D booster pack visual: a "tap to open" pack shown before a single pack's cards
        // reveal (see BuildBoosterPackSlot/ShowBoosterPackVisual/OnBoosterPackTapped/
        // PlayBoosterPackOpenAnimation). Only for single-pack opens; Bundle/Display still jump
        // straight to ShowGrid.
        //
        // Uses the Set's real pack art (CardSet.coverArt) when assigned, else a colored panel +
        // text label. Matches BigCellSize so it fills the same freed-up band as the reveal card.
        private static readonly Vector2 PackSize = BigCellSize;
        private static readonly Color PackBackgroundColor = new Color(0.45f, 0.28f, 0.55f);
        private GameObject _boosterPackSlot;
        private Image _boosterPackImage;
        private Text _boosterPackLabel;
        private Button _boosterPackButton;
        private CanvasGroup _boosterPackCanvasGroup;
        private bool _boosterPackAnimating;

        // NavBar lives as a sibling of this screen under the same Canvas, so SetTopControlsVisible
        // doesn't cover it by default - cached here so it can be locked too, preventing a fast
        // tapper from jumping screens mid-reveal.
        private Button[] _navBarButtons;

        private void Awake()
        {
            _title = transform.Find("Title").GetComponent<Text>();

            // Scene-baked bands only leave room for Title + SetButtonsRow - shifted down here at
            // runtime to fit a new LicenseButtonsRow above it (adjust-at-runtime rather than
            // touching the saved scene, same pattern used elsewhere in this project).
            _setButtonsRow = transform.Find("SetButtonsRow");
            var setButtonsRowRt = (RectTransform)_setButtonsRow;
            setButtonsRowRt.anchorMin = new Vector2(0f, 0.76f);
            setButtonsRowRt.anchorMax = new Vector2(1f, 0.83f);

            BuildLicenseButtonsRow();

            _openButton = transform.Find("OpenButton").GetComponent<Button>();
            _openButton.onClick.AddListener(OnOpenBoosterClicked);
            _openButton.interactable = false;
            var openButtonRt = (RectTransform)_openButton.transform;
            openButtonRt.anchorMin = new Vector2(0.15f, 0.67f);
            openButtonRt.anchorMax = new Vector2(0.85f, 0.75f);

            // Badge next to "Ouvrir un Booster" showing boosters banked for the selected Set (see
            // RefreshBankedBoosterLabel). Empty/hidden when nothing is banked.
            _bankedCountLabel = UIFactory.CreateText(transform, "", 20, Color.white);
            var bankedRt = (RectTransform)_bankedCountLabel.transform;
            bankedRt.anchorMin = new Vector2(0.86f, 0.67f);
            bankedRt.anchorMax = new Vector2(1f, 0.75f);
            bankedRt.offsetMin = Vector2.zero;
            bankedRt.offsetMax = Vector2.zero;

            BuildFastOpenToggle();

            _purchaseRow = transform.Find("PurchaseRow");
            var purchaseRowRt = (RectTransform)_purchaseRow;
            purchaseRowRt.anchorMin = new Vector2(0f, 0.58f);
            purchaseRowRt.anchorMax = new Vector2(1f, 0.65f);

            _revealAreaRoot = transform.Find("RevealArea").gameObject;
            _revealContent = transform.Find("RevealArea/Viewport/Content");
            _revealAreaRoot.SetActive(false);

            _bigCardSlot = transform.Find("BigCardSlot").gameObject;

            // UISceneBuilder bakes a tight 0.26-0.58 band (sized for the grid view). But
            // BigCardSlot only shows while SetTopControlsVisible(false) hides the rows above it,
            // so during a reveal the whole area between Title and ResultText is free -
            // re-anchored here to use it.
            var bigCardSlotRt = (RectTransform)_bigCardSlot.transform;
            bigCardSlotRt.anchorMin = new Vector2(0f, 0.10f);
            bigCardSlotRt.anchorMax = new Vector2(1f, 0.89f);
            bigCardSlotRt.offsetMin = Vector2.zero;
            bigCardSlotRt.offsetMax = Vector2.zero;

            _bigCell = UIFactory.CreateCardCell(_bigCardSlot.transform, BigCellSize);
            var bigRt = (RectTransform)_bigCell.Root.transform;
            bigRt.anchorMin = bigRt.anchorMax = new Vector2(0.5f, 0.5f);
            bigRt.pivot = new Vector2(0.5f, 0.5f);
            bigRt.anchoredPosition = Vector2.zero;
            _bigCell.RootButton.onClick.AddListener(OnBigCardTapped);
            _bigCardSlot.SetActive(false);

            BuildBoosterPackSlot();

            _resultText = transform.Find("ResultText").GetComponent<Text>();

            // Compacted toward the bottom of the screen so the bigger BigCellSize above has clearance.
            var resultRt = (RectTransform)_resultText.transform;
            resultRt.anchorMin = new Vector2(0f, 0f);
            resultRt.anchorMax = new Vector2(1f, 0.09f);
            resultRt.offsetMin = Vector2.zero;
            resultRt.offsetMax = Vector2.zero;

            // NavBar is a sibling of this screen's panel under the Canvas, not a child - hence a
            // sibling lookup instead of transform.Find(...).
            var navBar = transform.parent != null ? transform.parent.Find("NavBar") : null;
            _navBarButtons = navBar != null ? navBar.GetComponentsInChildren<Button>(true) : new Button[0];
        }

        /// <summary>Builds the License row at runtime, above SetButtonsRow (mirrors BinderScreen's
        /// runtime-built search bar). See BuildLicenseButtons for what populates it.</summary>
        private void BuildLicenseButtonsRow()
        {
            var rowGO = new GameObject("LicenseButtonsRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(transform, false);
            rowRt.anchorMin = new Vector2(0f, 0.83f);
            rowRt.anchorMax = new Vector2(1f, 0.90f);
            rowRt.offsetMin = Vector2.zero;
            rowRt.offsetMax = Vector2.zero;

            var layout = rowGO.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 12;
            layout.padding = new RectOffset(12, 12, 6, 6);

            _licenseButtonsRow = rowRt;
        }

        /// <summary>Builds a small checkbox for FastOpenSettings.Enabled (see BuildRevealQueue),
        /// next to the OpenButton instead of buried in ProfileScreen's Settings overlay. Fixed
        /// 44x44 box with a "Rapide" caption; only the checkmark toggles (see
        /// RefreshFastOpenToggleVisual), the box color stays neutral.</summary>
        private void BuildFastOpenToggle()
        {
            var rowGO = new GameObject("FastOpenToggle", typeof(RectTransform));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(transform, false);
            rowRt.anchorMin = new Vector2(0f, 0.67f);
            rowRt.anchorMax = new Vector2(0.145f, 0.75f);
            rowRt.offsetMin = Vector2.zero;
            rowRt.offsetMax = Vector2.zero;
            _fastOpenToggleRoot = rowGO;

            _fastOpenToggleButton = UIFactory.CreateButton(rowRt, "", CancelButtonColor, Color.white, OnToggleFastOpenClicked);
            var boxRt = (RectTransform)_fastOpenToggleButton.transform;
            boxRt.anchorMin = new Vector2(0.5f, 1f);
            boxRt.anchorMax = new Vector2(0.5f, 1f);
            boxRt.pivot = new Vector2(0.5f, 1f);
            boxRt.sizeDelta = new Vector2(44, 44);
            boxRt.anchoredPosition = Vector2.zero;

            _fastOpenToggleCheckmark = _fastOpenToggleButton.GetComponentInChildren<Text>();
            if (_fastOpenToggleCheckmark != null)
            {
                _fastOpenToggleCheckmark.text = "";
                _fastOpenToggleCheckmark.fontSize = 30;
                _fastOpenToggleCheckmark.fontStyle = FontStyle.Bold;
            }

            var caption = UIFactory.CreateText(rowRt, "Rapide", 15, Color.white);
            var captionRt = (RectTransform)caption.transform;
            captionRt.anchorMin = new Vector2(0f, 0f);
            captionRt.anchorMax = new Vector2(1f, 0.32f);
            captionRt.offsetMin = Vector2.zero;
            captionRt.offsetMax = Vector2.zero;

            RefreshFastOpenToggleVisual();
        }

        private void OnToggleFastOpenClicked()
        {
            FastOpenSettings.Enabled = !FastOpenSettings.Enabled;
            RefreshFastOpenToggleVisual();
        }

        /// <summary>Shows/hides the checkmark to match FastOpenSettings.Enabled; the box color
        /// itself never changes.</summary>
        private void RefreshFastOpenToggleVisual()
        {
            if (_fastOpenToggleCheckmark == null) return;
            _fastOpenToggleCheckmark.text = FastOpenSettings.Enabled ? "✓" : "";
        }

        /// <summary>Hides/shows the License row, Set row, Open button and Purchase row together so
        /// none are tappable while the big reveal card shows - this is also what frees up
        /// BigCardSlot's enlarged band (see Awake). Also disables (not hides, to avoid layout
        /// jumps) the NavBar, since it's a sibling outside this hierarchy and wouldn't otherwise
        /// be covered.</summary>
        private void SetTopControlsVisible(bool visible)
        {
            _licenseButtonsRow.gameObject.SetActive(visible);
            _setButtonsRow.gameObject.SetActive(visible);
            _openButton.gameObject.SetActive(visible);
            _bankedCountLabel.gameObject.SetActive(visible);
            _fastOpenToggleRoot.gameObject.SetActive(visible);
            _purchaseRow.gameObject.SetActive(visible);

            foreach (var navButton in _navBarButtons)
                if (navButton != null) navButton.interactable = visible;
        }

        private void OnEnable()
        {
            StartCoroutine(WaitForGameManagerThenBuild());
        }

        private IEnumerator WaitForGameManagerThenBuild()
        {
            while (GameManager.Instance == null || GameManager.Instance.CardDatabase == null)
                yield return null;

            if (!_buttonsBuilt) BuildLicenseButtons();
            if (!_purchaseButtonsBuilt) BuildPurchaseButtons();
        }

        /// <summary>Builds the Buy Booster/Bundle/Display buttons once with placeholder labels;
        /// RefreshPurchaseButtons fills in the real price/size per selected Set (see
        /// CardSet.boosterConfig) and hides the Bundle button when config.HasBundle is
        /// false.</summary>
        private void BuildPurchaseButtons()
        {
            _purchaseButtonsBuilt = true;

            _buyBoosterButton = UIFactory.CreateButton(_purchaseRow, "Booster", PurchaseButtonColor, Color.white, OnBuyBoosterClicked);
            UIFactory.SetPreferredSize(_buyBoosterButton.gameObject, 340, 80);

            _buyBundleButton = UIFactory.CreateButton(_purchaseRow, "Bundle", PurchaseButtonColor, Color.white, OnBuyBundleClicked);
            UIFactory.SetPreferredSize(_buyBundleButton.gameObject, 360, 80);

            _buyDisplayButton = UIFactory.CreateButton(_purchaseRow, "Display", PurchaseButtonColor, Color.white, OnBuyDisplayClicked);
            UIFactory.SetPreferredSize(_buyDisplayButton.gameObject, 400, 80);

            RefreshPurchaseButtons();
        }

        private void BuildLicenseButtons()
        {
            _buttonsBuilt = true;
            var licenses = GameManager.Instance.CardDatabase.GetAllLicenses();

            if (licenses.Count == 0)
            {
                _title.text = "Aucune Licence trouvee - assigne une Licence a un Set.";
                return;
            }

            foreach (var license in licenses)
            {
                var capturedLicense = license;
                var label = string.IsNullOrEmpty(license.displayName) ? license.name : license.displayName;
                var button = UIFactory.CreateButton(_licenseButtonsRow, label, SetButtonDefaultColor, Color.white, () => SelectLicense(capturedLicense));
                UIFactory.SetPreferredSize(button.gameObject, 260, 70);
                _licenseButtons.Add((capturedLicense, button));
            }

            SelectLicense(licenses[0]);
        }

        /// <summary>Rebuilds the Set row for the chosen License and auto-selects its first Set
        /// (mirrors BinderScreen's cascading Set -> Rarity filter).</summary>
        private void SelectLicense(License license)
        {
            _selectedLicense = license;

            foreach (var (buttonLicense, button) in _licenseButtons)
                button.GetComponent<Image>().color = buttonLicense == license ? SetButtonSelectedColor : SetButtonDefaultColor;

            foreach (var (_, button) in _setButtons)
                Destroy(button.gameObject);
            _setButtons.Clear();

            var setsInLicense = GameManager.Instance.CardDatabase.GetSetsInLicense(license);

            if (setsInLicense.Count == 0)
            {
                _title.text = "Aucun Set dans cette Licence - verifie la Card Database.";
                return;
            }

            foreach (var set in setsInLicense)
            {
                var capturedSet = set;
                var label = string.IsNullOrEmpty(set.displayName) ? set.name : set.displayName;
                var button = UIFactory.CreateButton(_setButtonsRow, label, SetButtonDefaultColor, Color.white, () => SelectSet(capturedSet));
                UIFactory.SetPreferredSize(button.gameObject, 260, 80);
                _setButtons.Add((capturedSet, button));
            }

            SelectSet(setsInLicense[0]);
        }

        private void SelectSet(CardSet set)
        {
            _selectedSet = set;

            foreach (var (buttonSet, button) in _setButtons)
                button.GetComponent<Image>().color = buttonSet == set ? SetButtonSelectedColor : SetButtonDefaultColor;

            _selectedSetLabel = string.IsNullOrEmpty(set.displayName) ? set.name : set.displayName;
            UpdateTitle();
            _bigCardSlot.SetActive(false);
            _revealAreaRoot.SetActive(false);
            RefreshDailyState();
        }

        /// <summary>Title always shows the current currency balance so a duplicate's auto-sell
        /// (CollectionManager.AddCards) is visible right after a booster opens.</summary>
        private void UpdateTitle()
        {
            int currency = GameManager.Instance != null ? GameManager.Instance.Collection.Currency : 0;
            _title.text = $"Booster : {_selectedSetLabel} - {currency} po";
            RefreshPurchaseButtons();
        }

        /// <summary>Refreshes the label text (price, sizes - per-Set via CardSet.boosterConfig) and
        /// enabled state of the Buy Booster/Bundle/Display buttons; Bundle is hidden entirely when
        /// config.HasBundle is false. Called via UpdateTitle whenever currency or Set selection
        /// changes.</summary>
        private void RefreshPurchaseButtons()
        {
            if (_buyBoosterButton == null || _buyDisplayButton == null || _buyBundleButton == null) return;

            var config = GameManager.Instance != null && _selectedSet != null
                ? GameManager.Instance.GetEffectiveBoosterConfig(_selectedSet)
                : null;

            if (config == null)
            {
                _buyBoosterButton.interactable = false;
                _buyBundleButton.gameObject.SetActive(false);
                _buyDisplayButton.interactable = false;
                return;
            }

            int currency = GameManager.Instance.Collection.Currency;

            var boosterLabel = _buyBoosterButton.GetComponentInChildren<Text>();
            if (boosterLabel != null) boosterLabel.text = $"Booster ({config.purchasePrice} po)";
            _buyBoosterButton.interactable = currency >= config.purchasePrice;

            // Optional tier: only shown for configs that actually define one (e.g. Pokemon).
            _buyBundleButton.gameObject.SetActive(config.HasBundle);
            if (config.HasBundle)
            {
                var bundleLabel = _buyBundleButton.GetComponentInChildren<Text>();
                if (bundleLabel != null) bundleLabel.text = $"Bundle x{config.bundleSize} ({config.BundlePrice} po)";
                _buyBundleButton.interactable = currency >= config.BundlePrice;
            }

            var displayLabel = _buyDisplayButton.GetComponentInChildren<Text>();
            if (displayLabel != null) displayLabel.text = $"Display x{config.displaySize} ({config.DisplayPrice} po)";
            _buyDisplayButton.interactable = currency >= config.DisplayPrice;
        }

        /// <summary>Enables/disables the Open button and shows the "come back tomorrow" message
        /// based on today's free-booster claim state (one per day, not per Set). Stays enabled if
        /// the player has bonus boosters (quests) or banked boosters (Display bought one-by-one) -
        /// banked ones take priority since they're already paid for.</summary>
        private void RefreshDailyState()
        {
            bool dailyClaimed = !debugIgnoreDailyLimit && GameManager.Instance != null && GameManager.Instance.Collection.HasClaimedFreeBoosterToday();
            int bonusBoosters = GameManager.Instance != null ? GameManager.Instance.Collection.BonusFreeBoosters : 0;
            int banked = GameManager.Instance != null && _selectedSet != null ? GameManager.Instance.Collection.GetBankedBoosterCount(_selectedSet) : 0;
            bool canOpen = banked > 0 || !dailyClaimed || bonusBoosters > 0;

            _openButton.interactable = _selectedSet != null && canOpen;

            if (banked > 0)
                _resultText.text = $"Tu as {banked} booster(s) en stock - touche \"Ouvrir un Booster\" pour en ouvrir un.";
            else if (!dailyClaimed)
                _resultText.text = "";
            else if (bonusBoosters > 0)
                _resultText.text = $"Booster bonus disponible ({bonusBoosters}) - gagne en remplissant tes quetes !";
            else
                _resultText.text = "Tu as deja ouvert ton booster gratuit du jour - reviens demain !";

            // Badge folds in bonus boosters (quests/login streak) too, not just banked ones, so
            // "En stock" reflects everything extra the player can open.
            RefreshBankedBoosterLabel(banked + bonusBoosters);
        }

        /// <summary>Updates the badge next to the Open button with the combined total of banked +
        /// bonus boosters; blank when zero. Also hidden during a reveal via
        /// SetTopControlsVisible.</summary>
        private void RefreshBankedBoosterLabel(int stockTotal)
        {
            if (_bankedCountLabel == null) return;
            _bankedCountLabel.text = stockTotal > 0 ? $"En stock :\n{stockTotal}" : "";
        }

        private void OnOpenBoosterClicked()
        {
            if (_selectedSet == null || GameManager.Instance == null) return;

            // Banked boosters were already paid for (PurchaseDisplayBanked), so they're drained
            // first and skip the daily/bonus gates entirely.
            int banked = GameManager.Instance.Collection.GetBankedBoosterCount(_selectedSet);
            if (banked > 0)
            {
                OpenBankedBoosterFlow();
                return;
            }

            bool dailyClaimed = !debugIgnoreDailyLimit && GameManager.Instance.Collection.HasClaimedFreeBoosterToday();
            bool useBonusBooster = dailyClaimed;
            if (dailyClaimed && GameManager.Instance.Collection.BonusFreeBoosters <= 0)
            {
                RefreshDailyState(); // shouldn't happen (button would be disabled) - safety net
                return;
            }

            _openButton.interactable = false;
            _resultText.text = "";
            _bigCardSlot.SetActive(false);
            _revealAreaRoot.SetActive(false);

            for (int i = _revealContent.childCount - 1; i >= 0; i--)
                Destroy(_revealContent.GetChild(i).gameObject);

            foreach (var boundCard in _revealBoundCards)
                CardArtworkLoader.Release(boundCard);
            _revealBoundCards.Clear();
            CardArtworkLoader.RequestSweep();

            _currentResults = GameManager.Instance.OpenBooster(_selectedSet);
            _revealIndex = 0;

            if (_currentResults.Count == 0)
            {
                _resultText.text = "Booster vide - verifie que le Set a des cartes avec une Rarete assignee, et que la Card Database a ete reconstruite (TCG Collector > Rebuild Card Database).";
                _openButton.interactable = true;
                return;
            }

            // Only consume a slot once the pull succeeds - a misconfigured Set shouldn't cost the
            // player anything. Uses the daily free slot first, falling back to a bonus booster
            // only if that's already claimed. Skipped in debug-bypass mode.
            if (!debugIgnoreDailyLimit)
            {
                if (useBonusBooster)
                    GameManager.Instance.Collection.TrySpendBonusFreeBooster();
                else
                    GameManager.Instance.Collection.MarkFreeBoosterClaimedToday();

                GameManager.Instance.Collection.Save();
            }

            ShowBoosterPackVisual();
        }

        /// <summary>Opens one already-paid-for banked booster (GameManager.OpenBankedBooster) -
        /// same sequential reveal as any single-booster open, skipping the currency/daily-limit
        /// bookkeeping since that happened at purchase time.</summary>
        private void OpenBankedBoosterFlow()
        {
            _openButton.interactable = false;
            _resultText.text = "";
            _bigCardSlot.SetActive(false);
            _revealAreaRoot.SetActive(false);

            for (int i = _revealContent.childCount - 1; i >= 0; i--)
                Destroy(_revealContent.GetChild(i).gameObject);

            foreach (var boundCard in _revealBoundCards)
                CardArtworkLoader.Release(boundCard);
            _revealBoundCards.Clear();
            CardArtworkLoader.RequestSweep();

            _currentResults = GameManager.Instance.OpenBankedBooster(_selectedSet);
            _revealIndex = 0;

            if (_currentResults == null || _currentResults.Count == 0)
            {
                _resultText.text = "Erreur a l'ouverture du booster en stock - verifie que le Set a des cartes avec une Rarete assignee.";
                _openButton.interactable = true;
                RefreshDailyState();
                return;
            }

            ShowBoosterPackVisual();
        }

        /// <summary>Builds the subset of _currentResults revealed one at a time (see
        /// FastOpenSettings.Enabled): everything when fast-open is off, or only non-filler slots
        /// when on (excludes config.commonSlotRarity/uncommonSlotRarity). Falls back to the full
        /// list for legacy configs (useSlotStructure false) or if every pull was filler. ShowGrid
        /// still iterates _currentResults in full either way - nothing is hidden from the
        /// collection, only the tap-by-tap presentation is shortened.</summary>
        private List<CollectionManager.CardPullResult> BuildRevealQueue()
        {
            if (!FastOpenSettings.Enabled) return _currentResults;

            var config = GameManager.Instance != null && _selectedSet != null
                ? GameManager.Instance.GetEffectiveBoosterConfig(_selectedSet)
                : null;

            if (config == null || !config.useSlotStructure) return _currentResults;

            var hits = new List<CollectionManager.CardPullResult>();
            foreach (var pull in _currentResults)
            {
                var rarity = pull.Card.rarity;
                bool isFixedFillerSlot = rarity != null && (rarity == config.commonSlotRarity || rarity == config.uncommonSlotRarity);
                if (!isFixedFillerSlot) hits.Add(pull);
            }

            return hits.Count > 0 ? hits : _currentResults;
        }

        /// <summary>Displays _revealQueue[_revealIndex] big, tap-to-advance mode - see
        /// BuildRevealQueue for which pulls actually end up in that queue.</summary>
        private void ShowSequentialCard()
        {
            _isReviewingFromGrid = false;
            var pull = _revealQueue[_revealIndex];
            ApplyPullToBigCell(pull);
            _resultText.text = $"{_revealIndex + 1} / {_revealQueue.Count} - touche la carte pour continuer";
            SetTopControlsVisible(false);
            _bigCardSlot.SetActive(true);
            StartCoroutine(PopIn(_bigCell.Root.transform));

            // Vibration is a per-Rarity flag (CardRarity.vibrateOnPull, set in the Inspector)
            // rather than an automatic rank cutoff, since games differ in how many rarities count
            // as a "hit". Toggleable globally via VibrationSettings.Enabled. Only fires here, not
            // when re-viewing a card from the grid, so it's once per pull.
            if (VibrationSettings.Enabled && pull.Card.rarity != null && pull.Card.rarity.vibrateOnPull)
                Handheld.Vibrate();
        }

        /// <summary>Displays a specific pull big, tap-to-return-to-grid mode (used when a grid
        /// cell is tapped to re-view a card after the reveal is finished).</summary>
        private void ShowBigCardFromGrid(CollectionManager.CardPullResult pull)
        {
            _isReviewingFromGrid = true;
            ApplyPullToBigCell(pull);
            _revealAreaRoot.SetActive(false);
            SetTopControlsVisible(false);
            _bigCardSlot.SetActive(true);
            StartCoroutine(PopIn(_bigCell.Root.transform));
        }

        private void ApplyPullToBigCell(CollectionManager.CardPullResult pull)
        {
            // Reused cell, never destroyed - release whatever it previously showed before binding
            // the next one (see ReleaseBigCardArtwork).
            ReleaseBigCardArtwork();

            var artwork = CardArtworkLoader.Acquire(pull.Card);
            if (artwork != null) _bigCardArtworkBound = pull.Card;

            _bigCell.Background.color = pull.Card.rarity != null ? pull.Card.rarity.accentColor : Color.white;
            UIFactory.SetArtwork(_bigCell, artwork);
            // Card artwork already has the name printed on it, so the dark name/badge bars would
            // be redundant once shown (see UIFactory.SetChromeForArtwork). The NOUVEAU/Doublon
            // badge still floats over the artwork regardless.
            UIFactory.SetChromeForArtwork(_bigCell, artwork != null);
            _bigCell.NameLabel.text = pull.Card.displayName;
            _bigCell.BadgeLabel.text = pull.WasNew ? "NOUVEAU" : $"Doublon +{pull.CurrencyEarned}";
            _bigCell.Root.transform.localScale = Vector3.zero;
        }

        /// <summary>Clears the big cell's Image reference before releasing the card it was bound
        /// to - the cell is reused, not destroyed, so nothing else drops the reference for
        /// us.</summary>
        private void ReleaseBigCardArtwork()
        {
            if (_bigCardArtworkBound == null) return;
            UIFactory.SetArtwork(_bigCell, null);
            CardArtworkLoader.Release(_bigCardArtworkBound);
            _bigCardArtworkBound = null;
        }

        // --- 2D booster pack visual (v1.9) ---

        /// <summary>Builds the "booster pack" visual shown before a single pack's cards reveal -
        /// tap to "tear it open" (OnBoosterPackTapped / PlayBoosterPackOpenAnimation), then
        /// ShowSequentialCard takes over. Starts as a colored panel + text placeholder;
        /// ShowBoosterPackVisual swaps in real pack art when assigned. Shares BigCardSlot's band
        /// since the two are never shown together.</summary>
        private void BuildBoosterPackSlot()
        {
            var slotGO = new GameObject("BoosterPackSlot", typeof(RectTransform));
            var slotRt = (RectTransform)slotGO.transform;
            slotRt.SetParent(transform, false);
            slotRt.anchorMin = new Vector2(0f, 0.10f);
            slotRt.anchorMax = new Vector2(1f, 0.89f);
            slotRt.offsetMin = Vector2.zero;
            slotRt.offsetMax = Vector2.zero;

            var packGO = new GameObject("Pack", typeof(RectTransform), typeof(Image), typeof(Button), typeof(CanvasGroup));
            var packRt = (RectTransform)packGO.transform;
            packRt.SetParent(slotRt, false);
            packRt.anchorMin = packRt.anchorMax = new Vector2(0.5f, 0.5f);
            packRt.pivot = new Vector2(0.5f, 0.5f);
            packRt.anchoredPosition = Vector2.zero;
            packRt.sizeDelta = PackSize;

            _boosterPackImage = packGO.GetComponent<Image>();
            _boosterPackImage.color = PackBackgroundColor;

            _boosterPackLabel = UIFactory.CreateText(packRt, "", 26, Color.white);
            _boosterPackLabel.fontStyle = FontStyle.Bold;

            var hint = UIFactory.CreateText(slotRt, "Touche pour ouvrir !", 22, new Color(1f, 1f, 1f, 0.7f));
            var hintRt = (RectTransform)hint.transform;
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0.08f);
            hintRt.offsetMin = Vector2.zero;
            hintRt.offsetMax = Vector2.zero;

            _boosterPackButton = packGO.GetComponent<Button>();
            _boosterPackButton.onClick.AddListener(OnBoosterPackTapped);

            _boosterPackCanvasGroup = packGO.GetComponent<CanvasGroup>();

            _boosterPackSlot = slotGO;
            _boosterPackSlot.SetActive(false);
        }

        /// <summary>Shows the 2D pack visual and waits for a tap, used at the three single-pack
        /// call sites (OnOpenBoosterClicked, OpenBankedBoosterFlow, OnBuyBoosterClicked). Bundle/
        /// Display purchases skip straight to ShowGrid. Uses the Set's real pack art
        /// (CardSet.coverArt) when assigned - replaces the flat color and hides the text label,
        /// since the art already carries its own branding - else falls back to the colored panel +
        /// text.</summary>
        private void ShowBoosterPackVisual()
        {
            SetTopControlsVisible(false);
            _bigCardSlot.SetActive(false);
            _revealAreaRoot.SetActive(false);

            var packArt = _selectedSet != null ? _selectedSet.coverArt : null;
            if (packArt != null)
            {
                _boosterPackImage.sprite = packArt;
                _boosterPackImage.color = Color.white;
                _boosterPackImage.preserveAspect = true;
                _boosterPackLabel.gameObject.SetActive(false);
            }
            else
            {
                _boosterPackImage.sprite = null;
                _boosterPackImage.color = PackBackgroundColor;
                _boosterPackLabel.gameObject.SetActive(true);
                _boosterPackLabel.text = $"{_selectedSetLabel}\nBooster";
            }

            _boosterPackButton.interactable = true;
            _boosterPackCanvasGroup.alpha = 1f;
            _boosterPackButton.transform.localScale = Vector3.one;
            _boosterPackButton.transform.localRotation = Quaternion.identity;
            _boosterPackSlot.SetActive(true);
        }

        private void OnBoosterPackTapped()
        {
            if (_boosterPackAnimating) return;
            StartCoroutine(PlayBoosterPackOpenAnimation());
        }

        /// <summary>Placeholder "tear it open" animation (shake, then punch-scale-and-fade-out),
        /// then hands off to the one-at-a-time reveal (BuildRevealQueue + ShowSequentialCard).
        /// Swap for a real tear/particle effect later.</summary>
        private IEnumerator PlayBoosterPackOpenAnimation()
        {
            _boosterPackAnimating = true;
            _boosterPackButton.interactable = false;

            var packTransform = _boosterPackButton.transform;

            float shakeDuration = 0.35f;
            float elapsed = 0f;
            while (elapsed < shakeDuration)
            {
                elapsed += Time.deltaTime;
                float angle = Mathf.Sin(elapsed * 40f) * 6f;
                packTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
                yield return null;
            }
            packTransform.localRotation = Quaternion.identity;

            float openDuration = 0.3f;
            elapsed = 0f;
            while (elapsed < openDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / openDuration);
                packTransform.localScale = Vector3.Lerp(Vector3.one, Vector3.one * 1.3f, t);
                _boosterPackCanvasGroup.alpha = 1f - t;
                yield return null;
            }

            packTransform.localScale = Vector3.one;
            _boosterPackCanvasGroup.alpha = 1f;
            _boosterPackSlot.SetActive(false);
            _boosterPackAnimating = false;

            _revealQueue = BuildRevealQueue();
            ShowSequentialCard();
        }

        private void OnBigCardTapped()
        {
            if (_isReviewingFromGrid)
            {
                ShowGrid();
                return;
            }

            _revealIndex++;
            if (_revealIndex >= _revealQueue.Count)
                ShowGrid();
            else
                ShowSequentialCard();
        }

        /// <summary>Final summary: every pulled card in a grid, tap one to bring it back up big.</summary>
        private void ShowGrid()
        {
            _bigCardSlot.SetActive(false);
            SetTopControlsVisible(true);

            // Destroy old cells first, then release the artwork they held (same ordering as
            // BinderScreen.Refresh).
            for (int i = _revealContent.childCount - 1; i >= 0; i--)
                Destroy(_revealContent.GetChild(i).gameObject);

            foreach (var boundCard in _revealBoundCards)
                CardArtworkLoader.Release(boundCard);
            _revealBoundCards.Clear();

            foreach (var pull in _currentResults)
            {
                var capturedPull = pull;

                var artwork = CardArtworkLoader.Acquire(capturedPull.Card);
                if (artwork != null) _revealBoundCards.Add(capturedPull.Card);

                var cell = UIFactory.CreateCardCell(_revealContent, GridCellSize);
                cell.Background.color = capturedPull.Card.rarity != null ? capturedPull.Card.rarity.accentColor : Color.white;
                UIFactory.SetArtwork(cell, artwork);
                UIFactory.SetChromeForArtwork(cell, artwork != null);
                cell.NameLabel.text = capturedPull.Card.displayName;
                cell.BadgeLabel.text = capturedPull.WasNew ? "NOUVEAU" : $"Doublon +{capturedPull.CurrencyEarned}";
                cell.RootButton.onClick.AddListener(() => ShowBigCardFromGrid(capturedPull));
            }

            CardArtworkLoader.RequestSweep();
            _revealAreaRoot.SetActive(true);
            UpdateTitle(); // refresh currency shown - duplicates were just auto-sold and/or a purchase was just spent

            var (owned, total) = GameManager.Instance.Collection.GetSetCompletionCounts(_selectedSet);
            float ratio = total == 0 ? 0f : (float)owned / total;
            string completionText = $"Completion du set : {owned}/{total} ({ratio:P0}) - touche une carte pour la revoir";

            // Refreshes _openButton from the daily/bonus/banked state alone, independent of what
            // triggered this grid, so buying something never mistakenly locks/unlocks the daily
            // slot.
            RefreshDailyState();
            int bankedRemaining = GameManager.Instance.Collection.GetBankedBoosterCount(_selectedSet);
            string tail = bankedRemaining > 0
                ? $"\nIl te reste {bankedRemaining} booster(s) en stock a ouvrir."
                : (_openButton.interactable ? "" : "\nProchain booster gratuit disponible demain.");
            _resultText.text = completionText + tail;
        }

        private void OnBuyBoosterClicked()
        {
            if (_selectedSet == null || GameManager.Instance == null) return;

            var results = GameManager.Instance.PurchaseBooster(_selectedSet);
            if (results == null)
            {
                _resultText.text = "Pas assez de po pour acheter un booster.";
                return;
            }

            _bigCardSlot.SetActive(false);
            _revealAreaRoot.SetActive(false);

            if (results.Count == 0)
            {
                _resultText.text = "Booster vide - verifie que le Set a des cartes avec une Rarete assignee, et que la Card Database a ete reconstruite (TCG Collector > Rebuild Card Database).";
                UpdateTitle(); // currency was spent even though the pack came back empty - reflect that, and re-enable the buy buttons
                RefreshDailyState();
                return;
            }

            _currentResults = results;
            _revealIndex = 0;
            ShowBoosterPackVisual();
        }

        private void OnBuyBundleClicked()
        {
            if (_selectedSet == null || GameManager.Instance == null) return;

            var results = GameManager.Instance.PurchaseBundle(_selectedSet);
            if (results == null)
            {
                _resultText.text = "Pas assez de po pour acheter un bundle.";
                return;
            }

            if (results.Count == 0)
            {
                _bigCardSlot.SetActive(false);
                _revealAreaRoot.SetActive(false);
                _resultText.text = "Bundle vide - verifie que le Set a des cartes avec une Rarete assignee, et que la Card Database a ete reconstruite (TCG Collector > Rebuild Card Database).";
                UpdateTitle();
                RefreshDailyState();
                return;
            }

            // A Bundle is several boosters at once - jump straight to the summary grid like a
            // Display, skipping the one-at-a-time reveal.
            _currentResults = results;
            _revealIndex = _currentResults.Count;
            ShowGrid();
        }

        /// <summary>Buying a Display asks the player how to open it first (see
        /// ShowDisplayChoiceOverlay); each choice handles its own currency spend, so opening the
        /// popup itself is free.</summary>
        private void OnBuyDisplayClicked()
        {
            if (_selectedSet == null || GameManager.Instance == null) return;

            ShowDisplayChoiceOverlay();
        }

        // --- Display "open all at once" vs "open one by one" popup ---

        private void ShowDisplayChoiceOverlay()
        {
            BuildDisplayChoiceOverlayIfNeeded();

            if (_displayChoiceMessageText != null)
            {
                var config = GameManager.Instance != null && _selectedSet != null
                    ? GameManager.Instance.GetEffectiveBoosterConfig(_selectedSet)
                    : null;
                _displayChoiceMessageText.text = config != null
                    ? $"{config.displaySize} boosters - {config.DisplayPrice} po au total."
                    : "";
            }

            _displayChoiceOverlay.SetActive(true);
        }

        private void HideDisplayChoiceOverlay()
        {
            if (_displayChoiceOverlay != null) _displayChoiceOverlay.SetActive(false);
        }

        /// <summary>Builds the Display choice popup once, lazily, parented to the Canvas root so
        /// it covers the whole screen including the NavBar.</summary>
        private void BuildDisplayChoiceOverlayIfNeeded()
        {
            if (_displayChoiceOverlay != null) return;

            var canvasRoot = transform.root;

            var overlayGO = new GameObject("DisplayChoiceOverlay", typeof(RectTransform), typeof(Image));
            var overlayRt = (RectTransform)overlayGO.transform;
            overlayRt.SetParent(canvasRoot, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            overlayGO.GetComponent<Image>().color = OverlayBackground;
            overlayGO.transform.SetAsLastSibling(); // always drawn on top of every screen/nav bar

            var boxGO = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var boxRt = (RectTransform)boxGO.transform;
            boxRt.SetParent(overlayRt, false);
            boxRt.anchorMin = new Vector2(0.5f, 0.5f);
            boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(820, 640);
            boxGO.GetComponent<Image>().color = BoxBackground;

            var boxLayout = boxGO.GetComponent<VerticalLayoutGroup>();
            boxLayout.padding = new RectOffset(30, 30, 30, 30);
            boxLayout.spacing = 20;
            boxLayout.childControlWidth = true;
            boxLayout.childControlHeight = true;
            boxLayout.childForceExpandWidth = true;
            boxLayout.childForceExpandHeight = false;

            var title = UIFactory.CreateText(boxRt, "Comment veux-tu ouvrir cette Display ?", 28, Color.white);
            title.fontStyle = FontStyle.Bold;
            UIFactory.SetPreferredSize(title.gameObject, 0, 90);

            _displayChoiceMessageText = UIFactory.CreateText(boxRt, "", 22, Color.white);
            UIFactory.SetPreferredSize(_displayChoiceMessageText.gameObject, 0, 70);

            var openAllButton = UIFactory.CreateButton(boxRt, "Tout ouvrir d'un coup", PurchaseButtonColor, Color.white, OnDisplayOpenAllChosen);
            UIFactory.SetPreferredSize(openAllButton.gameObject, 0, 100);

            var openOneByOneButton = UIFactory.CreateButton(boxRt, "Ouvrir une par une", PurchaseButtonColor, Color.white, OnDisplayOpenOneByOneChosen);
            UIFactory.SetPreferredSize(openOneByOneButton.gameObject, 0, 100);

            var cancelButton = UIFactory.CreateButton(boxRt, "Annuler", CancelButtonColor, Color.white, HideDisplayChoiceOverlay);
            UIFactory.SetPreferredSize(cancelButton.gameObject, 0, 80);

            _displayChoiceOverlay = overlayGO;
            _displayChoiceOverlay.SetActive(false);
        }

        private void OnDisplayOpenAllChosen()
        {
            HideDisplayChoiceOverlay();
            RunDisplayPurchaseOpenAll();
        }

        private void OnDisplayOpenOneByOneChosen()
        {
            HideDisplayChoiceOverlay();
            RunDisplayPurchaseBanked();
        }

        /// <summary>"Tout ouvrir d'un coup" - buys and reveals every booster in the Display
        /// immediately, jumping straight to the summary grid (same as a Bundle).</summary>
        private void RunDisplayPurchaseOpenAll()
        {
            if (_selectedSet == null || GameManager.Instance == null) return;

            var results = GameManager.Instance.PurchaseDisplay(_selectedSet);
            if (results == null)
            {
                _resultText.text = "Pas assez de po pour acheter une Display.";
                return;
            }

            if (results.Count == 0)
            {
                _bigCardSlot.SetActive(false);
                _revealAreaRoot.SetActive(false);
                _resultText.text = "Display vide - verifie que le Set a des cartes avec une Rarete assignee, et que la Card Database a ete reconstruite (TCG Collector > Rebuild Card Database).";
                UpdateTitle();
                RefreshDailyState();
                return;
            }

            // A Display is dozens of boosters at once - showing them one at a time would take
            // forever, so this jumps straight to the summary grid.
            _currentResults = results;
            _revealIndex = _currentResults.Count;
            ShowGrid();
        }

        /// <summary>"Ouvrir une par une" - buys the Display but banks its boosters unopened
        /// (GameManager.PurchaseDisplayBanked); drained one at a time via the normal "Ouvrir un
        /// Booster" button.</summary>
        private void RunDisplayPurchaseBanked()
        {
            if (_selectedSet == null || GameManager.Instance == null) return;

            bool success = GameManager.Instance.PurchaseDisplayBanked(_selectedSet);
            if (!success)
            {
                _resultText.text = "Pas assez de po pour acheter une Display.";
                return;
            }

            UpdateTitle();
            RefreshDailyState(); // shows "Tu as N booster(s) en stock..." and updates the badge
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