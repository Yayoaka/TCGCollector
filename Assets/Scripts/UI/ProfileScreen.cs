using System;
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
    /// Read-only recap screen: overall completion, then grouped by License (Riftbound, Solo
    /// Leveling, ...) and, under each, by Set. Single-player app, no account system - everything
    /// shown already lives in PlayerSaveData / CardDatabase, this screen just presents it.
    ///
    /// V0.x test-only: a "Reset" button (top-right) wipes the whole save (cards, currency, quest
    /// progress) for re-testing new content, gated behind a confirmation overlay since it's
    /// irreversible (see ShowResetConfirmation / BuildResetOverlayIfNeeded).
    ///
    /// Also an "Options" button (top-left) opening a Settings overlay - currently just a
    /// vibration toggle (see VibrationSettings). Built entirely at runtime (see
    /// BuildSettingsButtonAtRuntime) rather than patched into the saved scene.
    ///
    /// Expects this child hierarchy (built by UISceneBuilder, or patched on by
    /// ProfileResetButtonPatcher):
    ///   Header (Text)
    ///   ResetButton (Button) - optional; screen still works without it
    ///   ProfileList (built via UIFactory.CreateVerticalScrollList -> "ProfileList/Viewport/Content")
    /// </summary>
    public class ProfileScreen : MonoBehaviour
    {
        private static readonly Vector2 LicenseRowSize = new Vector2(0, 100);
        private static readonly Vector2 SetRowSize = new Vector2(0, 80);
        private static readonly Color LicenseRowBackground = new Color(0.22f, 0.28f, 0.4f);
        private static readonly Color SetRowBackground = new Color(0.16f, 0.16f, 0.2f);
        private static readonly Color OverlayBackground = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color BoxBackground = new Color(0.14f, 0.14f, 0.18f);
        private static readonly Color DangerButtonColor = new Color(0.55f, 0.15f, 0.15f);
        private static readonly Color CancelButtonColor = new Color(0.3f, 0.3f, 0.34f);
        private static readonly Color SettingsButtonColor = new Color(0.25f, 0.25f, 0.3f);

        private const string ResetWarningMessage =
            "Attention : cette action va reinitialiser toute ta collection.\n\n" +
            "Toutes les cartes trouvees, ta monnaie et la progression des quetes du jour " +
            "seront remises a zero. Cette action est irreversible.\n\n" +
            "Confirmer la reinitialisation ?";

        private Text _header;
        private Transform _listContent;
        private Button _resetButton;
        private GameObject _resetOverlay;

        private GameObject _settingsOverlay;
        private Text _vibrationToggleLabel;
        private Text _debugCurrencyButtonLabel;
        private Text _cloudStatusLabel;
        private Text _googleLoginButtonLabel;
        private bool _googleLoginInProgress;

        private void Awake()
        {
            _header = transform.Find("Header").GetComponent<Text>();
            _listContent = transform.Find("ProfileList/Viewport/Content");

            var resetButtonTransform = transform.Find("ResetButton");
            if (resetButtonTransform != null)
            {
                _resetButton = resetButtonTransform.GetComponent<Button>();
                if (_resetButton != null) _resetButton.onClick.AddListener(ShowResetConfirmation);
            }

            BuildSettingsButtonAtRuntime();
        }

        private void OnEnable()
        {
            StartCoroutine(WaitForGameManagerThenRefresh());
        }

        private IEnumerator WaitForGameManagerThenRefresh()
        {
            while (GameManager.Instance == null || GameManager.Instance.CardDatabase == null)
                yield return null;

            Refresh();
        }

        public void Refresh()
        {
            if (GameManager.Instance == null || GameManager.Instance.CardDatabase == null) return;

            var db = GameManager.Instance.CardDatabase;
            var collection = GameManager.Instance.Collection;

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            var (overallOwned, overallTotal) = collection.GetOverallCompletionCounts();
            float overallRatio = overallTotal == 0 ? 0f : (float)overallOwned / overallTotal;
            _header.text = $"Profil - {overallOwned}/{overallTotal} cartes ({overallRatio:P0}) - {collection.Currency} po";

            var handledSets = new HashSet<CardSet>();

            foreach (var license in db.GetAllLicenses())
            {
                var setsInLicense = db.GetSetsInLicense(license);

                int licenseOwned = 0;
                int licenseTotal = 0;
                foreach (var set in setsInLicense)
                {
                    var (owned, total) = collection.GetSetCompletionCounts(set);
                    licenseOwned += owned;
                    licenseTotal += total;
                }

                string licenseLabel = string.IsNullOrEmpty(license.displayName) ? license.name : license.displayName;
                BuildLicenseRow(licenseLabel, licenseOwned, licenseTotal);

                foreach (var set in setsInLicense)
                {
                    BuildSetRow(set, collection);
                    handledSets.Add(set);
                }
            }

            // Sets with no License assigned still need to show up - grouped under a catch-all
            // header at the end.
            var orphanSets = new List<CardSet>();
            foreach (var set in db.GetAllSets())
            {
                if (!handledSets.Contains(set)) orphanSets.Add(set);
            }

            if (orphanSets.Count > 0)
            {
                int otherOwned = 0;
                int otherTotal = 0;
                foreach (var set in orphanSets)
                {
                    var (owned, total) = collection.GetSetCompletionCounts(set);
                    otherOwned += owned;
                    otherTotal += total;
                }

                BuildLicenseRow("Autres sets", otherOwned, otherTotal);
                foreach (var set in orphanSets)
                    BuildSetRow(set, collection);
            }
        }

        private void BuildLicenseRow(string label, int owned, int total)
        {
            float ratio = total == 0 ? 0f : (float)owned / total;
            string text = total == 0 ? label : $"{label} - {owned}/{total} ({ratio:P0})";
            BuildRow(text, LicenseRowSize, LicenseRowBackground, 28, FontStyle.Bold, 0f);
        }

        private void BuildSetRow(CardSet set, TCGCollector.Systems.CollectionManager collection)
        {
            string setLabel = string.IsNullOrEmpty(set.displayName) ? set.name : set.displayName;
            var (owned, total) = collection.GetSetCompletionCounts(set);

            string text = total == 0
                ? $"{setLabel} - aucune carte pour l'instant"
                : $"{setLabel} - {owned}/{total} ({(total == 0 ? 0f : (float)owned / total):P0})";

            // Indented relative to its License row so the hierarchy reads clearly without a
            // tree/foldout widget.
            BuildRow(text, SetRowSize, SetRowBackground, 22, FontStyle.Normal, 60f);
        }

        private void BuildRow(string text, Vector2 size, Color background, int fontSize, FontStyle style, float leftIndent)
        {
            var rowGO = new GameObject("Row", typeof(RectTransform), typeof(Image));
            var rowRt = (RectTransform)rowGO.transform;
            rowRt.SetParent(_listContent, false);
            rowGO.GetComponent<Image>().color = background;
            UIFactory.SetPreferredSize(rowGO, size.x, size.y);

            var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = (RectTransform)labelGO.transform;
            labelRt.SetParent(rowRt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(20 + leftIndent, 4);
            labelRt.offsetMax = new Vector2(-20, -4);

            var label = labelGO.GetComponent<Text>();
            label.font = UIFactory.DefaultFont;
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // --- Reset collection (V0.x test feature) ---

        private void ShowResetConfirmation()
        {
            BuildResetOverlayIfNeeded();
            _resetOverlay.SetActive(true);
        }

        private void BuildResetOverlayIfNeeded()
        {
            if (_resetOverlay != null) return;

            // Parented to the Canvas root so it covers the whole screen, including the nav bar.
            var canvasRoot = transform.root;

            var overlayGO = new GameObject("ResetConfirmOverlay", typeof(RectTransform), typeof(Image));
            var overlayRt = (RectTransform)overlayGO.transform;
            overlayRt.SetParent(canvasRoot, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            overlayGO.GetComponent<Image>().color = OverlayBackground;
            overlayGO.transform.SetAsLastSibling(); // always drawn on top of every screen/nav bar

            var boxGO = new GameObject("Box", typeof(RectTransform), typeof(Image));
            var boxRt = (RectTransform)boxGO.transform;
            boxRt.SetParent(overlayRt, false);
            boxRt.anchorMin = new Vector2(0.5f, 0.5f);
            boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(800, 560);
            boxGO.GetComponent<Image>().color = BoxBackground;

            var messageGO = new GameObject("MessageText", typeof(RectTransform), typeof(Text));
            var messageRt = (RectTransform)messageGO.transform;
            messageRt.SetParent(boxRt, false);
            messageRt.anchorMin = new Vector2(0f, 0.28f);
            messageRt.anchorMax = new Vector2(1f, 1f);
            messageRt.offsetMin = new Vector2(30, 0);
            messageRt.offsetMax = new Vector2(-30, -20);

            var messageText = messageGO.GetComponent<Text>();
            messageText.font = UIFactory.DefaultFont;
            messageText.text = ResetWarningMessage;
            messageText.fontSize = 26;
            messageText.color = Color.white;
            messageText.alignment = TextAnchor.MiddleCenter;
            messageText.horizontalOverflow = HorizontalWrapMode.Wrap;
            messageText.verticalOverflow = VerticalWrapMode.Overflow;

            var buttonRowGO = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var buttonRowRt = (RectTransform)buttonRowGO.transform;
            buttonRowRt.SetParent(boxRt, false);
            buttonRowRt.anchorMin = new Vector2(0f, 0f);
            buttonRowRt.anchorMax = new Vector2(1f, 0.22f);
            buttonRowRt.offsetMin = new Vector2(30, 20);
            buttonRowRt.offsetMax = new Vector2(-30, 0);

            var buttonRowLayout = buttonRowGO.GetComponent<HorizontalLayoutGroup>();
            buttonRowLayout.spacing = 20;
            buttonRowLayout.childControlWidth = true;
            buttonRowLayout.childControlHeight = true;
            buttonRowLayout.childForceExpandWidth = true;
            buttonRowLayout.childForceExpandHeight = true;

            UIFactory.CreateButton(buttonRowRt, "Annuler", CancelButtonColor, Color.white, HideResetOverlay);
            UIFactory.CreateButton(buttonRowRt, "Confirmer", DangerButtonColor, Color.white, OnConfirmReset);

            _resetOverlay = overlayGO;
            _resetOverlay.SetActive(false);
        }

        private void HideResetOverlay()
        {
            if (_resetOverlay != null) _resetOverlay.SetActive(false);
        }

        private void OnConfirmReset()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Collection.ResetAll();
                Refresh();
            }

            HideResetOverlay();
        }

        // --- Settings (top-left "Options" button - currently just the vibration toggle) ---

        /// <summary>Builds the "Options" button at runtime, top-left, mirroring the scene-built
        /// Reset button on the right. Narrows the Header's left edge to make room, same as
        /// ProfileResetButtonPatcher does for Reset on the right.</summary>
        private void BuildSettingsButtonAtRuntime()
        {
            if (_header != null)
            {
                var headerRt = (RectTransform)_header.transform;
                headerRt.anchorMin = new Vector2(0.3f, headerRt.anchorMin.y);
            }

            var buttonGO = new GameObject("SettingsButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRt = (RectTransform)buttonGO.transform;
            buttonRt.SetParent(transform, false);
            buttonRt.anchorMin = new Vector2(0.02f, 0.925f);
            buttonRt.anchorMax = new Vector2(0.26f, 0.985f);
            buttonRt.offsetMin = Vector2.zero;
            buttonRt.offsetMax = Vector2.zero;
            buttonGO.GetComponent<Image>().color = SettingsButtonColor;
            buttonGO.GetComponent<Button>().onClick.AddListener(ShowSettingsOverlay);

            UIFactory.CreateText(buttonRt, "Options", 24, Color.white);
        }

        private void ShowSettingsOverlay()
        {
            BuildSettingsOverlayIfNeeded();
            RefreshVibrationToggleLabel();
            RefreshDebugCurrencyButtonLabel();
            RefreshCloudStatusLabel();
            RefreshGoogleLoginLabel();
            _settingsOverlay.SetActive(true);
        }

        private void BuildSettingsOverlayIfNeeded()
        {
            if (_settingsOverlay != null) return;

            // Parented to the Canvas root, same as BuildResetOverlayIfNeeded above.
            var canvasRoot = transform.root;

            var overlayGO = new GameObject("SettingsOverlay", typeof(RectTransform), typeof(Image));
            var overlayRt = (RectTransform)overlayGO.transform;
            overlayRt.SetParent(canvasRoot, false);
            overlayRt.anchorMin = Vector2.zero;
            overlayRt.anchorMax = Vector2.one;
            overlayRt.offsetMin = Vector2.zero;
            overlayRt.offsetMax = Vector2.zero;
            overlayGO.GetComponent<Image>().color = OverlayBackground;
            overlayGO.transform.SetAsLastSibling();

            var boxGO = new GameObject("Box", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var boxRt = (RectTransform)boxGO.transform;
            boxRt.SetParent(overlayRt, false);
            boxRt.anchorMin = new Vector2(0.5f, 0.5f);
            boxRt.anchorMax = new Vector2(0.5f, 0.5f);
            boxRt.pivot = new Vector2(0.5f, 0.5f);
            boxRt.sizeDelta = new Vector2(700, 640); // sized for 4 rows: Vibration, debug currency, Google, Fermer
            boxGO.GetComponent<Image>().color = BoxBackground;

            var boxLayout = boxGO.GetComponent<VerticalLayoutGroup>();
            boxLayout.padding = new RectOffset(30, 30, 30, 30);
            boxLayout.spacing = 24;
            boxLayout.childControlWidth = true;
            boxLayout.childControlHeight = true;
            boxLayout.childForceExpandWidth = true;
            boxLayout.childForceExpandHeight = false;

            var title = UIFactory.CreateText(boxRt, "Parametres", 30, Color.white);
            title.fontStyle = FontStyle.Bold;
            UIFactory.SetPreferredSize(title.gameObject, 0, 60);

            var vibrationButton = UIFactory.CreateButton(boxRt, "Vibration", CancelButtonColor, Color.white, OnToggleVibrationClicked);
            UIFactory.SetPreferredSize(vibrationButton.gameObject, 0, 90);
            _vibrationToggleLabel = vibrationButton.GetComponentInChildren<Text>();

            // "Fast Open" moved onto BoosterOpenScreen itself (checkbox next to "Ouvrir un
            // Booster") - see BoosterOpenScreen.BuildFastOpenToggle / FastOpenSettings.

            // V0.x test-only: grants currency directly so testers can reach Display/Bundle
            // purchase flows without grinding duplicates. Remove before treating pricing as final.
            var debugCurrencyButton = UIFactory.CreateButton(boxRt, "", CancelButtonColor, Color.white, OnAddDebugCurrencyClicked);
            UIFactory.SetPreferredSize(debugCurrencyButton.gameObject, 0, 90);
            _debugCurrencyButtonLabel = debugCurrencyButton.GetComponentInChildren<Text>();
            RefreshDebugCurrencyButtonLabel();

            // V0.x test-only: shows whether the background cloud save (CloudSyncService) is
            // connected - informational only, refreshed each time this overlay opens
            // (RefreshCloudStatusLabel) rather than via a live subscription.
            _cloudStatusLabel = UIFactory.CreateText(boxRt, "", 18, Color.white);
            _cloudStatusLabel.alignment = TextAnchor.MiddleCenter;
            UIFactory.SetPreferredSize(_cloudStatusLabel.gameObject, 0, 40);

            // Runs the desktop Google OAuth flow and links it onto the current anonymous account
            // (GameManager.LinkGoogleAccountAsync). Needs a Google Client Id set in the Inspector,
            // else fails gracefully with a message in this label.
            var googleLoginButton = UIFactory.CreateButton(boxRt, "", CancelButtonColor, Color.white, OnGoogleLoginClicked);
            UIFactory.SetPreferredSize(googleLoginButton.gameObject, 0, 90);
            _googleLoginButtonLabel = googleLoginButton.GetComponentInChildren<Text>();

            var closeButton = UIFactory.CreateButton(boxRt, "Fermer", CancelButtonColor, Color.white, HideSettingsOverlay);
            UIFactory.SetPreferredSize(closeButton.gameObject, 0, 80);

            _settingsOverlay = overlayGO;
            _settingsOverlay.SetActive(false);
        }

        private void HideSettingsOverlay()
        {
            if (_settingsOverlay != null) _settingsOverlay.SetActive(false);
        }

        private void OnToggleVibrationClicked()
        {
            VibrationSettings.Enabled = !VibrationSettings.Enabled;
            RefreshVibrationToggleLabel();
        }

        private void RefreshVibrationToggleLabel()
        {
            if (_vibrationToggleLabel == null) return;
            _vibrationToggleLabel.text = VibrationSettings.Enabled ? "Vibration : Activee" : "Vibration : Desactivee";
        }

        /// <summary>V0.x test-only: grants 1000 po directly so testers can reach the Bundle/
        /// Display purchase flows without grinding. Saves immediately and refreshes this button's
        /// label and the Profile header behind the overlay.</summary>
        private void OnAddDebugCurrencyClicked()
        {
            if (GameManager.Instance == null) return;

            GameManager.Instance.Collection.AddCurrency(1000);
            GameManager.Instance.Collection.Save();

            RefreshDebugCurrencyButtonLabel();
            Refresh(); // updates the "- N po" shown in the Profile header behind the overlay
        }

        private void RefreshDebugCurrencyButtonLabel()
        {
            if (_debugCurrencyButtonLabel == null) return;
            int currency = GameManager.Instance != null ? GameManager.Instance.Collection.Currency : 0;
            _debugCurrencyButtonLabel.text = $"+1000 po (debug) - solde : {currency} po";
        }

        /// <summary>V0.x test-only: shows CloudSyncService's current status in plain French so a
        /// tester can tell at a glance whether cloud save is working, without digging through
        /// logs.</summary>
        private void RefreshCloudStatusLabel()
        {
            if (_cloudStatusLabel == null) return;

            var cloudSync = GameManager.Instance != null ? GameManager.Instance.CloudSync : null;
            var status = cloudSync != null ? cloudSync.CurrentStatus : CloudSyncService.Status.Idle;

            switch (status)
            {
                case CloudSyncService.Status.Synced:
                    _cloudStatusLabel.text = "Cloud : connecte";
                    break;
                case CloudSyncService.Status.SignedIn:
                    _cloudStatusLabel.text = "Cloud : synchronisation...";
                    break;
                case CloudSyncService.Status.Connecting:
                    _cloudStatusLabel.text = "Cloud : connexion...";
                    break;
                case CloudSyncService.Status.Offline:
                    _cloudStatusLabel.text = "Cloud : indisponible (sauvegarde locale uniquement)";
                    break;
                case CloudSyncService.Status.Error:
                    _cloudStatusLabel.text = "Cloud : erreur (sauvegarde locale uniquement)";
                    break;
                default:
                    _cloudStatusLabel.text = "Cloud : en attente";
                    break;
            }
        }

        // --- Google Sign-In (test phase - see GameManager.LinkGoogleAccountAsync / GoogleDesktopAuth) ---

        private void RefreshGoogleLoginLabel()
        {
            if (_googleLoginButtonLabel == null) return;
            if (_googleLoginInProgress) return; // don't stomp on "Connexion..." mid-flow

            bool linked = GameManager.Instance != null && GameManager.Instance.IsGoogleLinked;
            _googleLoginButtonLabel.text = linked ? "Connecte avec Google" : "Se connecter avec Google";
        }

        /// <summary>Opens the system browser for Google sign-in (GoogleDesktopAuth) and links the
        /// result onto the current account. async void is intentional - a UI click handler has
        /// nothing to await it, and all failure paths are caught below.</summary>
        private async void OnGoogleLoginClicked()
        {
            if (_googleLoginInProgress || GameManager.Instance == null) return;

            _googleLoginInProgress = true;
            if (_googleLoginButtonLabel != null) _googleLoginButtonLabel.text = "Connexion... (regarde ton navigateur)";

            try
            {
                await GameManager.Instance.LinkGoogleAccountAsync();
                if (_googleLoginButtonLabel != null) _googleLoginButtonLabel.text = "Connecte avec Google";
                RefreshCloudStatusLabel();
            }
            catch (TimeoutException e)
            {
                Debug.LogWarning($"[ProfileScreen] Google sign-in timed out: {e.Message}");
                if (_googleLoginButtonLabel != null) _googleLoginButtonLabel.text = "Temps ecoule - reessaie";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProfileScreen] Google sign-in failed: {e.Message}");
                if (_googleLoginButtonLabel != null) _googleLoginButtonLabel.text = "Echec - reessaie (voir logs)";
            }
            finally
            {
                _googleLoginInProgress = false;
            }
        }
    }
}
