using TCGCollector.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// Generates the placeholder UI (Canvas, EventSystem, Booster/Binder screens, nav bar) directly
    /// into the open scene as real, inspectable GameObjects. Static chrome is built once here;
    /// dynamic content (card cells, filter buttons) is instantiated at runtime by
    /// BinderScreen/BoosterOpenScreen.
    ///
    /// Run once via TCG Collector > Build UI Scene (V0.1). Refuses to run if a UIRoot already
    /// exists - delete "TCG UI Canvas" and "EventSystem" first to regenerate from scratch.
    /// </summary>
    public static class UISceneBuilder
    {
        private const float NavBarHeight = 160f;
        private static readonly Color PanelBackground = new Color(0.08f, 0.08f, 0.1f);
        private static readonly Color NavBarColor = new Color(0.05f, 0.05f, 0.07f);
        private static readonly Color ButtonColor = new Color(0.25f, 0.25f, 0.3f);

        [MenuItem("TCG Collector/Build UI Scene (V0.1)")]
        public static void Build()
        {
            if (Object.FindFirstObjectByType<UIRoot>() != null)
            {
                Debug.LogError("[UISceneBuilder] A UIRoot already exists in this scene. Delete the existing 'TCG UI Canvas' (and 'EventSystem' if you want a full reset) before rebuilding.");
                return;
            }

            EnsureEventSystem();

            var canvasGO = new GameObject("TCG UI Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Build TCG UI");

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            BuildNavBar(canvasGO.transform);
            BuildBoosterScreen(canvasGO.transform);
            BuildBinderScreen(canvasGO.transform);
            BuildQuestScreen(canvasGO.transform);
            BuildShopScreen(canvasGO.transform);
            BuildProfileScreen(canvasGO.transform);

            canvasGO.AddComponent<UIRoot>();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[UISceneBuilder] UI scene built. Assign the GameManager's references if you haven't, then press Play.");
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));

            // ENABLE_INPUT_SYSTEM is set when Active Input Handling uses the Input System Package -
            // StandaloneInputModule throws at runtime in that mode, so use InputSystemUIInputModule.
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif

            Undo.RegisterCreatedObjectUndo(go, "Build TCG UI");
        }

        private static void BuildNavBar(Transform canvasTransform)
        {
            var navGO = new GameObject("NavBar", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            var navRt = (RectTransform)navGO.transform;
            navRt.SetParent(canvasTransform, false);
            navRt.anchorMin = new Vector2(0f, 0f);
            navRt.anchorMax = new Vector2(1f, 0f);
            navRt.pivot = new Vector2(0.5f, 0f);
            navRt.sizeDelta = new Vector2(0f, NavBarHeight);
            navRt.anchoredPosition = Vector2.zero;
            navGO.GetComponent<Image>().color = NavBarColor;

            var layout = navGO.GetComponent<HorizontalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);

            UIFactory.CreateButton(navRt, "Boosters", ButtonColor, Color.white, null);
            UIFactory.CreateButton(navRt, "Classeur", ButtonColor, Color.white, null);
            UIFactory.CreateButton(navRt, "Quetes", ButtonColor, Color.white, null);
            UIFactory.CreateButton(navRt, "Boutique", ButtonColor, Color.white, null);
            UIFactory.CreateButton(navRt, "Profil", ButtonColor, Color.white, null);
        }

        private static void BuildBoosterScreen(Transform canvasTransform)
        {
            var panel = UIFactory.CreateInsetPanel(canvasTransform, "BoosterScreen", PanelBackground, NavBarHeight);

            CreatePositionedText(panel, "Title", "Choisis un Set", 40, Color.white,
                new Vector2(0f, 0.90f), new Vector2(1f, 0.98f));

            CreatePositionedRow(panel, "SetButtonsRow", new Vector2(0f, 0.80f), new Vector2(1f, 0.90f));

            CreatePositionedButton(panel, "OpenButton", "Ouvrir un Booster",
                new Vector2(0.15f, 0.71f), new Vector2(0.85f, 0.79f));

            // Buy Booster / Buy Display buttons are built at runtime by BoosterOpenScreen (needs
            // the live price from BoosterConfig) - this just creates the empty row container.
            CreatePositionedRow(panel, "PurchaseRow", new Vector2(0f, 0.61f), new Vector2(1f, 0.69f));

            // Vertical multi-column grid (not a horizontal strip) so all 10 pulled cards fit as
            // 2 scrollable rows instead of requiring a sideways swipe. 5 columns keeps it a
            // scroll view so it still works if packSize is raised later.
            var revealContent = UIFactory.CreateVerticalScrollGrid(panel, "RevealArea", new Vector2(190, 260), new Vector2(16, 16), 5);
            var revealRootRt = revealContent.parent.parent.GetComponent<RectTransform>(); // Content -> Viewport -> RevealArea
            revealRootRt.anchorMin = new Vector2(0f, 0.26f);
            revealRootRt.anchorMax = new Vector2(1f, 0.58f);
            revealRootRt.offsetMin = Vector2.zero;
            revealRootRt.offsetMax = Vector2.zero;
            revealRootRt.gameObject.SetActive(false); // shown only once the sequential reveal finishes

            // Single big card slot for the one-at-a-time reveal (tap = next) and for re-viewing a
            // card from the final grid. BoosterOpenScreen creates the card visual inside this and
            // toggles it vs. RevealArea.
            var bigCardSlotGO = new GameObject("BigCardSlot", typeof(RectTransform));
            var bigCardSlotRt = (RectTransform)bigCardSlotGO.transform;
            bigCardSlotRt.SetParent(panel, false);
            bigCardSlotRt.anchorMin = new Vector2(0f, 0.26f);
            bigCardSlotRt.anchorMax = new Vector2(1f, 0.58f);
            bigCardSlotRt.offsetMin = Vector2.zero;
            bigCardSlotRt.offsetMax = Vector2.zero;
            bigCardSlotGO.SetActive(false);

            CreatePositionedText(panel, "ResultText", "", 24, Color.white,
                new Vector2(0f, 0.02f), new Vector2(1f, 0.13f));

            var screenComponent = panel.gameObject.AddComponent<BoosterOpenScreen>();
            _ = screenComponent; // component wires itself up via Awake()
        }

        private static void BuildBinderScreen(Transform canvasTransform)
        {
            var panel = UIFactory.CreateInsetPanel(canvasTransform, "BinderScreen", PanelBackground, NavBarHeight);
            panel.gameObject.SetActive(false); // UIRoot shows the Booster screen by default

            CreatePositionedText(panel, "Header", "Classeur", 34, Color.white,
                new Vector2(0f, 0.92f), new Vector2(1f, 0.99f));

            CreatePositionedRow(panel, "SetFilterRow", new Vector2(0f, 0.85f), new Vector2(1f, 0.92f));
            CreatePositionedRow(panel, "RarityFilterRow", new Vector2(0f, 0.78f), new Vector2(1f, 0.85f));

            // "CardGrid" created directly under 'panel' to match BinderScreen.Awake()'s lookup path,
            // then repositioned into a band instead of filling the whole panel.
            var gridContent = UIFactory.CreateVerticalScrollGrid(panel, "CardGrid", new Vector2(300, 420), new Vector2(20, 20), 3);
            var gridRootRt = gridContent.parent.parent.GetComponent<RectTransform>(); // Content -> Viewport -> CardGrid
            gridRootRt.anchorMin = new Vector2(0f, 0f);
            gridRootRt.anchorMax = new Vector2(1f, 0.78f);
            gridRootRt.offsetMin = Vector2.zero;
            gridRootRt.offsetMax = Vector2.zero;

            panel.gameObject.AddComponent<BinderScreen>();
        }

        private static void BuildQuestScreen(Transform canvasTransform)
        {
            var panel = UIFactory.CreateInsetPanel(canvasTransform, "QuestScreen", PanelBackground, NavBarHeight);
            panel.gameObject.SetActive(false); // UIRoot shows the Booster screen by default

            CreatePositionedText(panel, "Header", "Quêtes du jour", 34, Color.white,
                new Vector2(0f, 0.92f), new Vector2(1f, 0.99f));

            // "QuestList" created directly under 'panel' to match QuestScreen.Awake()'s lookup path.
            var questListContent = UIFactory.CreateVerticalScrollList(panel, "QuestList", 16f);
            var questListRootRt = questListContent.parent.parent.GetComponent<RectTransform>(); // Content -> Viewport -> QuestList
            questListRootRt.anchorMin = new Vector2(0f, 0f);
            questListRootRt.anchorMax = new Vector2(1f, 0.92f);
            questListRootRt.offsetMin = Vector2.zero;
            questListRootRt.offsetMax = Vector2.zero;

            panel.gameObject.AddComponent<QuestScreen>();
        }

        private static void BuildProfileScreen(Transform canvasTransform)
        {
            var panel = UIFactory.CreateInsetPanel(canvasTransform, "ProfileScreen", PanelBackground, NavBarHeight);
            panel.gameObject.SetActive(false); // UIRoot shows the Booster screen by default

            CreatePositionedText(panel, "Header", "Profil", 30, Color.white,
                new Vector2(0f, 0.92f), new Vector2(1f, 0.99f));

            // "ProfileList" created directly under 'panel' to match ProfileScreen.Awake()'s lookup path.
            var profileListContent = UIFactory.CreateVerticalScrollList(panel, "ProfileList", 10f);
            var profileListRootRt = profileListContent.parent.parent.GetComponent<RectTransform>(); // Content -> Viewport -> ProfileList
            profileListRootRt.anchorMin = new Vector2(0f, 0f);
            profileListRootRt.anchorMax = new Vector2(1f, 0.92f);
            profileListRootRt.offsetMin = Vector2.zero;
            profileListRootRt.offsetMax = Vector2.zero;

            panel.gameObject.AddComponent<ProfileScreen>();
        }

        /// <summary>Adds the Boutique (v1.8) screen + its NavBar button to an already-built scene,
        /// without rebuilding everything. Safe to re-run: refuses if "ShopScreen" already exists.
        /// Run once via TCG Collector > Add Shop Screen (V1.8) after the Shop* scripts are in place
        /// and compiled - the button/screen do nothing until UIRoot.cs knows about them.</summary>
        [MenuItem("TCG Collector/Add Shop Screen (V1.8)")]
        public static void AddShopScreen()
        {
            var uiRoot = Object.FindFirstObjectByType<UIRoot>();
            if (uiRoot == null)
            {
                Debug.LogError("[UISceneBuilder] No UIRoot found - build the base UI scene first (TCG Collector > Build UI Scene (V0.1)).");
                return;
            }

            var canvasTransform = uiRoot.transform;
            if (canvasTransform.Find("ShopScreen") != null)
            {
                Debug.LogError("[UISceneBuilder] A 'ShopScreen' already exists under the UI canvas - nothing to add.");
                return;
            }

            var navBarRt = canvasTransform.Find("NavBar") as RectTransform;
            if (navBarRt == null)
            {
                Debug.LogError("[UISceneBuilder] No 'NavBar' found under the UI canvas.");
                return;
            }

            Undo.RegisterFullObjectHierarchyUndo(canvasTransform.gameObject, "Add Shop Screen");

            var shopButton = UIFactory.CreateButton(navBarRt, "Boutique", ButtonColor, Color.white, null);
            // Insert before "Profil" so nav order reads Boosters / Classeur / Quetes / Boutique / Profil.
            var profilButtonTransform = navBarRt.Find("Profil Button");
            if (profilButtonTransform != null)
                shopButton.transform.SetSiblingIndex(profilButtonTransform.GetSiblingIndex());

            BuildShopScreen(canvasTransform);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[UISceneBuilder] Shop screen added. Make sure UIRoot.cs has been patched to wire up the 'Boutique' button (see patch_v18_uiroot.ps1) - the button won't do anything until it has.");
        }

        private static void BuildShopScreen(Transform canvasTransform)
        {
            var panel = UIFactory.CreateInsetPanel(canvasTransform, "ShopScreen", PanelBackground, NavBarHeight);
            panel.gameObject.SetActive(false); // UIRoot shows the Booster screen by default

            CreatePositionedText(panel, "Header", "Boutique", 34, Color.white,
                new Vector2(0f, 0.92f), new Vector2(1f, 0.99f));

            // "ShopList" created directly under 'panel' to match ShopScreen.Awake()'s lookup path.
            var shopListContent = UIFactory.CreateVerticalScrollList(panel, "ShopList", 16f);
            var shopListRootRt = shopListContent.parent.parent.GetComponent<RectTransform>(); // Content -> Viewport -> ShopList
            shopListRootRt.anchorMin = new Vector2(0f, 0f);
            shopListRootRt.anchorMax = new Vector2(1f, 0.92f);
            shopListRootRt.offsetMin = Vector2.zero;
            shopListRootRt.offsetMax = Vector2.zero;

            panel.gameObject.AddComponent<ShopScreen>();
        }

        // --- Small positioned-element helpers (band placement within a panel). ---

        private static Text CreatePositionedText(Transform parent, string name, string content, int fontSize, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = UIFactory.DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button CreatePositionedButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = ButtonColor;

            CreatePositionedText(rt, "Text", label, 30, Color.white, Vector2.zero, Vector2.one);

            return go.GetComponent<Button>();
        }

        private static RectTransform CreatePositionedRow(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 12;
            layout.padding = new RectOffset(12, 12, 6, 6);

            return rt;
        }
    }
}
