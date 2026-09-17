using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.UI
{
    /// <summary>
    /// Helper library for building uGUI elements purely from code, since this project has no
    /// hand-made prefabs/art yet. Every screen and UISceneBuilder go through these methods, so
    /// swapping in real art later only means changing these internals, not every call site.
    /// </summary>
    public static class UIFactory
    {
        private static Font _cachedFont;

        public static Font DefaultFont
        {
            get
            {
                if (_cachedFont != null) return _cachedFont;
                // Newer Unity versions renamed the built-in "Arial.ttf" resource to
                // "LegacyRuntime.ttf"; try both for compatibility with older editors.
                _cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_cachedFont == null) _cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return _cachedFont;
            }
        }

        public struct CardCellRefs
        {
            public GameObject Root;
            public Image Background;
            public Image Artwork;
            public Text NameLabel;
            public Text BadgeLabel;
            public Button RootButton;
            public GameObject NameBackdrop;
            public Image BadgeBackdropImage;
        }

        /// <summary>Shows a card's real artwork if it has one, otherwise leaves the flat
        /// rarity-tinted Background showing as a placeholder.</summary>
        public static void SetArtwork(CardCellRefs cell, Sprite sprite)
        {
            cell.Artwork.sprite = sprite;
            cell.Artwork.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        /// <summary>Call once per cell right after SetArtwork. Real artwork already has the card
        /// name printed on it, so the dark NameBackdrop bar is hidden once artwork shows; the
        /// BadgeBackdrop bar is hidden too, but its BadgeLabel text (e.g. "NOUVEAU"/"x3") stays
        /// visible over the art (it has an Outline for legibility - see CreateCardCell). With no
        /// artwork, both bars stay in their normal state showing "???" over the rarity tint.</summary>
        public static void SetChromeForArtwork(CardCellRefs cell, bool hasArtwork)
        {
            cell.NameBackdrop.SetActive(!hasArtwork);
            cell.BadgeBackdropImage.enabled = !hasArtwork;
        }

        /// <summary>A RectTransform that fills its parent completely (0/0 offsets, stretch anchors).</summary>
        public static RectTransform CreateStretchRect(Transform parent, string name, params System.Type[] extraComponents)
        {
            var types = new System.Type[extraComponents.Length + 1];
            types[0] = typeof(RectTransform);
            System.Array.Copy(extraComponents, 0, types, 1, extraComponents.Length);

            var go = new GameObject(name, types);
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var rt = CreateStretchRect(parent, name, typeof(Image));
            rt.GetComponent<Image>().color = color;
            return rt;
        }

        /// <summary>Panel filling its parent but leaving 'bottomInset' pixels free at the bottom,
        /// so screens don't sit under the bottom nav bar.</summary>
        public static RectTransform CreateInsetPanel(Transform parent, string name, Color color, float bottomInset)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(0, bottomInset);
            rt.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            return rt;
        }

        public static Text CreateText(Transform parent, string content, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var rt = CreateStretchRect(parent, "Text", typeof(Text));
            var text = rt.GetComponent<Text>();
            text.font = DefaultFont;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        public static Button CreateButton(Transform parent, string label, Color background, Color textColor, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = background;

            var button = go.GetComponent<Button>();
            if (onClick != null) button.onClick.AddListener(onClick);

            CreateText(rt, label, 26, textColor);

            return button;
        }

        public static LayoutElement SetPreferredSize(GameObject go, float width, float height)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null) element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = height;
            return element;
        }

        /// <summary>A scroll view whose content grows downward with N fixed-width columns. Returns
        /// the content RectTransform - instantiate cells as its children.</summary>
        public static RectTransform CreateVerticalScrollGrid(Transform parent, string name, Vector2 cellSize, Vector2 spacing, int columns)
        {
            var scrollRt = CreateStretchRect(parent, name, typeof(Image), typeof(ScrollRect));
            scrollRt.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);

            var viewportRt = CreateStretchRect(scrollRt, "Viewport", typeof(RectMask2D));

            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = (RectTransform)contentGO.transform;
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var grid = contentGO.GetComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = new RectOffset(20, 20, 20, 20);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, columns);
            contentGO.AddComponent<ResponsiveCardGrid>();

            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = scrollRt.GetComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.viewport = viewportRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // Unity 6's input pipeline reports mouse-wheel deltas of +/-1 per notch instead of the
            // old +/-120, so the default scrollSensitivity (tuned for the old scale) barely moves
            // the list. Scale it to one row per notch instead.
            scrollRect.scrollSensitivity = cellSize.y + spacing.y;

            return contentRt;
        }

        /// <summary>A scroll view whose content grows downward as a single column of full-width
        /// rows, each sized via its own LayoutElement (see SetPreferredSize) rather than a fixed
        /// grid cell size. Returns the content RectTransform to parent rows under.</summary>
        public static RectTransform CreateVerticalScrollList(Transform parent, string name, float spacing)
        {
            var scrollRt = CreateStretchRect(parent, name, typeof(Image), typeof(ScrollRect));
            scrollRt.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);

            var viewportRt = CreateStretchRect(scrollRt, "Viewport", typeof(RectMask2D));

            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = (RectTransform)contentGO.transform;
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var layout = contentGO.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = spacing;
            layout.padding = new RectOffset(20, 20, 20, 20);

            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = scrollRt.GetComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.viewport = viewportRt;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // Same Unity 6 mouse-wheel fix as CreateVerticalScrollGrid; rows vary in height here,
            // so this is just a representative value (~one row per notch).
            scrollRect.scrollSensitivity = 90f;

            return contentRt;
        }

        /// <summary>A scroll view whose content grows sideways in a single row. Used for the
        /// booster-opening "reveal strip". Returns the content RectTransform.</summary>
        public static RectTransform CreateHorizontalScrollGrid(Transform parent, string name, Vector2 cellSize, Vector2 spacing)
        {
            var scrollRt = CreateStretchRect(parent, name, typeof(Image), typeof(ScrollRect));
            scrollRt.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);

            var viewportRt = CreateStretchRect(scrollRt, "Viewport", typeof(RectMask2D));

            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = (RectTransform)contentGO.transform;
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 0.5f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var grid = contentGO.GetComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = new RectOffset(20, 20, 20, 20);
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 1;

            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = scrollRt.GetComponent<ScrollRect>();
            scrollRect.content = contentRt;
            scrollRect.viewport = viewportRt;
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // Same Unity 6 mouse-wheel fix as CreateVerticalScrollGrid, horizontal axis.
            scrollRect.scrollSensitivity = cellSize.x + spacing.x;

            return contentRt;
        }

        /// <summary>Converts an existing empty RectTransform (e.g. a plain HorizontalLayoutGroup
        /// band like BinderScreen's "RarityFilterRow") into a horizontally-scrollable single-row
        /// strip in place, since a bare layout group can't reach children that overflow its width.
        /// Returns the "Content" transform to parent new children under. Idempotent.</summary>
        public static RectTransform MakeHorizontallyScrollable(RectTransform existingRoot, Vector2 cellSize, Vector2 spacing)
        {
            var already = existingRoot.Find("Viewport/Content");
            if (already != null) return (RectTransform)already;

            // Remove any existing layout group first - it would fight with the ScrollRect/
            // Viewport/Content structure below.
            foreach (var lg in existingRoot.GetComponents<LayoutGroup>())
                Object.Destroy(lg);

            if (existingRoot.GetComponent<Image>() == null)
                existingRoot.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);

            var scrollRect = existingRoot.GetComponent<ScrollRect>();
            if (scrollRect == null) scrollRect = existingRoot.gameObject.AddComponent<ScrollRect>();

            var viewportRt = CreateStretchRect(existingRoot, "Viewport", typeof(RectMask2D));

            var contentGO = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            var contentRt = (RectTransform)contentGO.transform;
            contentRt.SetParent(viewportRt, false);
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 0.5f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var grid = contentGO.GetComponent<GridLayoutGroup>();
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.padding = new RectOffset(12, 12, 6, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedRowCount;
            grid.constraintCount = 1;

            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.content = contentRt;
            scrollRect.viewport = viewportRt;
            scrollRect.horizontal = true;
            scrollRect.vertical = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            // Same Unity 6 mouse-wheel fix as CreateVerticalScrollGrid.
            scrollRect.scrollSensitivity = cellSize.x + spacing.x;

            return contentRt;
        }

        /// <summary>A single-line text input with a placeholder, built from legacy UnityEngine.UI
        /// (no TextMeshPro dependency). Used by BinderScreen's search bar.</summary>
        public static InputField CreateInputField(Transform parent, string placeholder)
        {
            var go = new GameObject("SearchField", typeof(RectTransform), typeof(Image), typeof(InputField));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.16f, 0.16f, 0.2f);

            var textAreaRt = CreateStretchRect(rt, "Text Area", typeof(RectMask2D));

            var placeholderGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            var placeholderRt = (RectTransform)placeholderGO.transform;
            placeholderRt.SetParent(textAreaRt, false);
            placeholderRt.anchorMin = Vector2.zero;
            placeholderRt.anchorMax = Vector2.one;
            placeholderRt.offsetMin = new Vector2(14, 4);
            placeholderRt.offsetMax = new Vector2(-14, -4);
            var placeholderText = placeholderGO.GetComponent<Text>();
            placeholderText.font = DefaultFont;
            placeholderText.text = placeholder;
            placeholderText.fontSize = 26;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.color = new Color(1f, 1f, 1f, 0.5f);
            placeholderText.alignment = TextAnchor.MiddleLeft;

            var valueTextGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var valueTextRt = (RectTransform)valueTextGO.transform;
            valueTextRt.SetParent(textAreaRt, false);
            valueTextRt.anchorMin = Vector2.zero;
            valueTextRt.anchorMax = Vector2.one;
            valueTextRt.offsetMin = new Vector2(14, 4);
            valueTextRt.offsetMax = new Vector2(-14, -4);
            var valueText = valueTextGO.GetComponent<Text>();
            valueText.font = DefaultFont;
            valueText.fontSize = 26;
            valueText.color = Color.white;
            valueText.alignment = TextAnchor.MiddleLeft;
            valueText.supportRichText = false;

            var inputField = go.GetComponent<InputField>();
            inputField.targetGraphic = go.GetComponent<Image>();
            inputField.textComponent = valueText;
            inputField.placeholder = placeholderText;
            inputField.lineType = InputField.LineType.SingleLine;

            return inputField;
        }

        /// <summary>A single card visual: colored background + name label + small corner badge
        /// (used for "NOUVEAU"/"Doublon" or duplicate counts).</summary>
        public static CardCellRefs CreateCardCell(Transform parent, Vector2 size)
        {
            var go = new GameObject("CardCell", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;

            var background = go.GetComponent<Image>();
            var rootButton = go.GetComponent<Button>();
            background.color = new Color(0.2f, 0.2f, 0.2f);

            // Sits on top of Background and hides it once a sprite is assigned (see SetArtwork).
            // Starts transparent, since an Image with no sprite still draws an opaque rectangle.
            var artworkGO = new GameObject("Artwork", typeof(RectTransform), typeof(Image));
            var artworkRt = (RectTransform)artworkGO.transform;
            artworkRt.SetParent(rt, false);
            artworkRt.anchorMin = Vector2.zero;
            artworkRt.anchorMax = Vector2.one;
            artworkRt.offsetMin = Vector2.zero;
            artworkRt.offsetMax = Vector2.zero;
            var artwork = artworkGO.GetComponent<Image>();
            artwork.preserveAspect = true;
            artwork.raycastTarget = false;
            artwork.color = new Color(1f, 1f, 1f, 0f);

            // Dark semi-transparent backdrop keeps the name readable regardless of the rarity's
            // accent color. SetChromeForArtwork hides this whole bar once real artwork (which
            // already has the name printed on it) is showing.
            var nameBackdropGO = new GameObject("NameBackdrop", typeof(RectTransform), typeof(Image));
            var nameBackdropRt = (RectTransform)nameBackdropGO.transform;
            nameBackdropRt.SetParent(rt, false);
            nameBackdropRt.anchorMin = new Vector2(0f, 0f);
            nameBackdropRt.anchorMax = new Vector2(1f, 0.3f);
            nameBackdropRt.offsetMin = Vector2.zero;
            nameBackdropRt.offsetMax = Vector2.zero;
            nameBackdropGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var nameRt = new GameObject("NameLabel", typeof(RectTransform), typeof(Text));
            var nameLabel = nameRt.GetComponent<Text>();
            var nameTransform = (RectTransform)nameRt.transform;
            nameTransform.SetParent(nameBackdropRt, false);
            nameTransform.anchorMin = Vector2.zero;
            nameTransform.anchorMax = Vector2.one;
            nameTransform.offsetMin = new Vector2(6, 4);
            nameTransform.offsetMax = new Vector2(-6, -4);
            nameLabel.font = DefaultFont;
            nameLabel.fontSize = 20;
            nameLabel.color = Color.white;
            nameLabel.alignment = TextAnchor.MiddleCenter;
            nameLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            nameLabel.verticalOverflow = VerticalWrapMode.Truncate;

            // SetChromeForArtwork hides only this backdrop bar once artwork shows - badgeLabel
            // (below) stays visible so pull/duplicate feedback doesn't disappear with it.
            var badgeBackdropGO = new GameObject("BadgeBackdrop", typeof(RectTransform), typeof(Image));
            var badgeBackdropRt = (RectTransform)badgeBackdropGO.transform;
            badgeBackdropRt.SetParent(rt, false);
            badgeBackdropRt.anchorMin = new Vector2(0f, 0.85f);
            badgeBackdropRt.anchorMax = new Vector2(1f, 1f);
            badgeBackdropRt.offsetMin = Vector2.zero;
            badgeBackdropRt.offsetMax = Vector2.zero;
            var badgeBackdropImage = badgeBackdropGO.GetComponent<Image>();
            badgeBackdropImage.color = new Color(0f, 0f, 0f, 0.45f);

            var badgeRt = new GameObject("BadgeLabel", typeof(RectTransform), typeof(Text));
            var badgeLabel = badgeRt.GetComponent<Text>();
            var badgeTransform = (RectTransform)badgeRt.transform;
            badgeTransform.SetParent(badgeBackdropRt, false);
            badgeTransform.anchorMin = Vector2.zero;
            badgeTransform.anchorMax = Vector2.one;
            // Margin from the top-right corner: real artwork often prints a cost/power icon there,
            // so the badge is nudged inward instead of stamping directly on top of it.
            badgeTransform.offsetMin = new Vector2(4, 0);
            badgeTransform.offsetMax = new Vector2(-16, -14);
            badgeLabel.font = DefaultFont;
            badgeLabel.fontSize = 18;
            badgeLabel.fontStyle = FontStyle.Bold;
            badgeLabel.color = Color.yellow;
            badgeLabel.alignment = TextAnchor.UpperRight;
            // Overflow rather than Wrap: Wrap can break into one letter per line if the box's
            // computed width is briefly wrong during layout - Overflow guarantees a single line.
            badgeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            badgeLabel.text = "";

            // Keeps the badge text legible over artwork of any color once BadgeBackdrop is hidden.
            var badgeOutline = badgeLabel.gameObject.AddComponent<Outline>();
            badgeOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            badgeOutline.effectDistance = new Vector2(1.5f, -1.5f);

            return new CardCellRefs
            {
                Root = go,
                Background = background,
                Artwork = artwork,
                NameLabel = nameLabel,
                BadgeLabel = badgeLabel,
                RootButton = rootButton,
                NameBackdrop = nameBackdropGO,
                BadgeBackdropImage = badgeBackdropImage
            };
        }
    }
}
