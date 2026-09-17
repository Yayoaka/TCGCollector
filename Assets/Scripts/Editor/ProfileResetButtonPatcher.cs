using TCGCollector.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.EditorTools
{
    /// <summary>
    /// One-shot patch for scenes built by UISceneBuilder before the Reset button existed (V0.x
    /// test feature - see ProfileScreen.ShowResetConfirmation). Finds the existing "ProfileScreen"
    /// panel, narrows its Header, and adds a "ResetButton" child the same way UISceneBuilder would
    /// have. Safe to re-run: does nothing if ResetButton already exists.
    /// </summary>
    public static class ProfileResetButtonPatcher
    {
        private static readonly Color ButtonColor = new Color(0.25f, 0.25f, 0.3f);

        [MenuItem("TCG Collector/Add Reset Button To Profile (V0.x)")]
        public static void Patch()
        {
            var profileScreen = Object.FindFirstObjectByType<ProfileScreen>(FindObjectsInactive.Include);
            if (profileScreen == null)
            {
                Debug.LogError("[ProfileResetButtonPatcher] No ProfileScreen found in the open scene. Build the UI first (TCG Collector > Build UI Scene).");
                return;
            }

            var panel = profileScreen.transform;

            var existing = panel.Find("ResetButton");
            if (existing != null)
            {
                Debug.Log("[ProfileResetButtonPatcher] ResetButton already exists on ProfileScreen - nothing to do.");
                return;
            }

            var headerTransform = panel.Find("Header");
            if (headerTransform != null)
            {
                var headerRt = (RectTransform)headerTransform;
                Undo.RecordObject(headerRt, "Narrow Profile Header");
                headerRt.anchorMax = new Vector2(0.7f, headerRt.anchorMax.y);
            }
            else
            {
                Debug.LogWarning("[ProfileResetButtonPatcher] No 'Header' child found under ProfileScreen - Reset button added anyway, but it may overlap the header text.");
            }

            var buttonGO = new GameObject("ResetButton", typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonGO, "Add Profile Reset Button");
            var buttonRt = (RectTransform)buttonGO.transform;
            buttonRt.SetParent(panel, false);
            buttonRt.anchorMin = new Vector2(0.74f, 0.925f);
            buttonRt.anchorMax = new Vector2(0.98f, 0.985f);
            buttonRt.offsetMin = Vector2.zero;
            buttonRt.offsetMax = Vector2.zero;
            buttonGO.GetComponent<Image>().color = ButtonColor;

            var labelGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(buttonRt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var label = labelGO.GetComponent<Text>();
            label.font = UIFactory.DefaultFont;
            label.text = "Reset";
            label.fontSize = 26;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[ProfileResetButtonPatcher] ResetButton added to ProfileScreen. Press Play and open the Profil tab to test it.");
        }
    }
}
