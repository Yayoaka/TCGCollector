using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.UI
{
    /// <summary>
    /// Recomputes a fixed-column GridLayoutGroup's cell width from the actual available width at
    /// runtime, instead of a cellSize tuned for the CanvasScaler's reference resolution - avoids
    /// clipped columns on aspect ratios narrower than that reference. Attach alongside the
    /// GridLayoutGroup (CreateVerticalScrollGrid does this automatically). Cell height scales with
    /// width to keep the aspect ratio.
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    [RequireComponent(typeof(RectTransform))]
    public class ResponsiveCardGrid : MonoBehaviour
    {
        private GridLayoutGroup _grid;
        private RectTransform _rect;
        private float _aspectRatio; // cellSize.y / cellSize.x, fixed at the design size
        private float _lastWidth = -1f;

        private void Awake()
        {
            _grid = GetComponent<GridLayoutGroup>();
            _rect = GetComponent<RectTransform>();
            _aspectRatio = _grid.cellSize.x > 0f ? _grid.cellSize.y / _grid.cellSize.x : 1f;
        }

        private void OnEnable()
        {
            _lastWidth = -1f; // force a recompute
            Recompute();
        }

        private void Update()
        {
            float width = _rect.rect.width;
            if (width > 0f && !Mathf.Approximately(width, _lastWidth))
            {
                _lastWidth = width;
                Recompute();
            }
        }

        private void Recompute()
        {
            float width = _rect.rect.width;
            if (width <= 0f) return;

            int columns = Mathf.Max(1, _grid.constraintCount);
            float horizontalPadding = _grid.padding.left + _grid.padding.right;
            float totalSpacing = _grid.spacing.x * (columns - 1);
            float cellWidth = (width - horizontalPadding - totalSpacing) / columns;
            cellWidth = Mathf.Max(40f, cellWidth); // sane floor so cards never collapse to nothing

            _grid.cellSize = new Vector2(cellWidth, cellWidth * _aspectRatio);
            LayoutRebuilder.MarkLayoutForRebuild(_rect);
        }
    }
}
