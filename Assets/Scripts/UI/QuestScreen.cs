using System.Collections;
using System.Collections.Generic;
using TCGCollector.Core;
using TCGCollector.Systems;
using UnityEngine;
using UnityEngine.UI;

namespace TCGCollector.UI
{
    /// <summary>
    /// Daily quests: two fixed quests every day (open a booster, log in), claimable once their
    /// target is reached (see QuestManager.Catalog). Expects this child hierarchy, built by
    /// UISceneBuilder:
    ///   Header (Text)
    ///   QuestList (via UIFactory.CreateVerticalScrollList -> "QuestList/Viewport/Content")
    /// </summary>
    public class QuestScreen : MonoBehaviour
    {
        private static readonly Vector2 RowSize = new Vector2(0, 150);
        private static readonly Color RowBackground = new Color(0.18f, 0.18f, 0.22f);
        private static readonly Color RowCompleteBackground = new Color(0.2f, 0.4f, 0.22f);
        private static readonly Color ClaimButtonColor = new Color(0.35f, 0.55f, 0.9f);
        private static readonly Color ClaimedButtonColor = new Color(0.3f, 0.3f, 0.3f);

        private Text _header;
        private Transform _listContent;

        private void Awake()
        {
            _header = transform.Find("Header").GetComponent<Text>();
            _listContent = transform.Find("QuestList/Viewport/Content");
        }

        private void OnEnable()
        {
            StartCoroutine(WaitForGameManagerThenRefresh());
        }

        private IEnumerator WaitForGameManagerThenRefresh()
        {
            while (GameManager.Instance == null || GameManager.Instance.Quests == null)
                yield return null;

            Refresh();
        }

        public void Refresh()
        {
            if (GameManager.Instance == null || GameManager.Instance.Quests == null) return;

            var quests = GameManager.Instance.Quests.GetTodaysQuests();

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            foreach (var status in quests)
                BuildRow(status);

            int streak = GameManager.Instance.Quests.LoginStreakDays;
            _header.text = $"Quetes du jour - {GameManager.Instance.Collection.Currency} po - Serie : {streak}/{QuestManager.StreakGoalDays} jours";
        }

        private void BuildRow(QuestStatus status)
        {
            var rowGO = new GameObject(status.Definition.questId + " Row", typeof(RectTransform), typeof(Image));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(_listContent, false);
            rowGO.GetComponent<Image>().color = status.IsComplete ? RowCompleteBackground : RowBackground;
            UIFactory.SetPreferredSize(rowGO, RowSize.x, RowSize.y);

            int shownProgress = Mathf.Min(status.Progress, status.Definition.target);
            string progressLabel = $"{status.Definition.title}\n{shownProgress}/{status.Definition.target}";

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(rowRt, false);
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(0.7f, 1f);
            labelRt.offsetMin = new Vector2(20, 8);
            labelRt.offsetMax = new Vector2(-10, -8);

            var label = labelGO.GetComponent<Text>();
            label.font = UIFactory.DefaultFont;
            label.text = progressLabel;
            label.fontSize = 24;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;

            bool canClaim = status.IsComplete && !status.Claimed;

            // A quest can pay out currency AND boosters at once, so list every non-zero reward on
            // its own line rather than picking just one.
            string buttonLabel;
            if (status.Claimed)
            {
                buttonLabel = "Reclame";
            }
            else
            {
                var rewardLines = new List<string>();
                if (status.Definition.rewardBoosters > 0) rewardLines.Add($"+{status.Definition.rewardBoosters} Booster");
                if (status.Definition.rewardCurrency > 0) rewardLines.Add($"+{status.Definition.rewardCurrency} po");
                buttonLabel = string.Join("\n", rewardLines);
            }

            var buttonColor = status.Claimed ? ClaimedButtonColor : ClaimButtonColor;

            var buttonGO = new GameObject("ClaimButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRt = (RectTransform)buttonGO.transform;
            buttonRt.SetParent(rowRt, false);
            buttonRt.anchorMin = new Vector2(0.72f, 0.15f);
            buttonRt.anchorMax = new Vector2(0.98f, 0.85f);
            buttonRt.offsetMin = Vector2.zero;
            buttonRt.offsetMax = Vector2.zero;
            buttonGO.GetComponent<Image>().color = buttonColor;

            var buttonTextGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var buttonTextRt = (RectTransform)buttonTextGO.transform;
            buttonTextRt.SetParent(buttonRt, false);
            buttonTextRt.anchorMin = Vector2.zero;
            buttonTextRt.anchorMax = Vector2.one;
            buttonTextRt.offsetMin = Vector2.zero;
            buttonTextRt.offsetMax = Vector2.zero;

            var buttonText = buttonTextGO.GetComponent<Text>();
            buttonText.font = UIFactory.DefaultFont;
            buttonText.text = buttonLabel;
            buttonText.fontSize = 22;
            buttonText.color = Color.white;
            buttonText.alignment = TextAnchor.MiddleCenter;

            var claimButton = buttonGO.GetComponent<Button>();
            claimButton.interactable = canClaim;
            string questId = status.Definition.questId; // capture for the closure below
            claimButton.onClick.AddListener(() => OnClaimClicked(questId));
        }

        private void OnClaimClicked(string questId)
        {
            if (GameManager.Instance == null || GameManager.Instance.Quests == null) return;

            if (GameManager.Instance.Quests.TryClaimReward(questId))
            {
                GameManager.Instance.Collection.Save();
                Refresh();
            }
        }
    }
}
