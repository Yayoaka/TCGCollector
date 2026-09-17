using UnityEngine;

namespace TCGCollector.Data
{
    /// <summary>Holds the Google OAuth "Desktop app" Client ID/Secret for optional Google Sign-In
    /// (see GameManager, GoogleDesktopAuth). Kept as its own asset rather than raw fields on
    /// GameManager so the real values never get serialized into a scene file that's tracked in
    /// git - create your own instance via Assets > Create > TCG Collector > Google Auth Config,
    /// fill in your real credentials, and keep that .asset file (and its .meta) out of version
    /// control (see .gitignore).</summary>
    [CreateAssetMenu(fileName = "GoogleAuthConfig", menuName = "TCG Collector/Google Auth Config", order = 10)]
    public class GoogleAuthConfig : ScriptableObject
    {
        [Tooltip("Client ID cree dans Google Cloud Console (type 'Desktop app').")]
        public string clientId;

        [Tooltip("Optionnel - un client 'Desktop app' Google n'en a normalement pas besoin (PKCE suffit).")]
        public string clientSecret;
    }
}
