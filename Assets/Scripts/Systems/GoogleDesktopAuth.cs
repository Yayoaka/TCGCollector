using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TCGCollector.Systems
{
    /// <summary>
    /// Google "Sign in" for desktop (Windows/Mac/Linux standalone + Editor): the OAuth 2.0
    /// Authorization Code + PKCE loopback flow (RFC 8252). Opens the system browser to Google's
    /// consent screen, listens on a random localhost port for the redirect, exchanges the code
    /// for tokens, and returns the id_token that Unity Authentication's SignInWithGoogleAsync/
    /// LinkWithGoogleAsync expect.
    ///
    /// Desktop-only by design - mobile builds should use the native Google Sign-In SDK instead.
    /// ClientId/ClientSecret are set once by GameManager.Awake; ClientSecret is optional (PKCE
    /// covers it) but sent along if present.
    /// </summary>
    public static class GoogleDesktopAuth
    {
        public static string ClientId;
        public static string ClientSecret;

        private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string Scope = "openid email profile";

        /// <summary>How long to wait for the browser redirect before giving up, so a player who
        /// closes the tab or walks away doesn't leave the listener stuck forever.</summary>
        private static readonly TimeSpan RedirectTimeout = TimeSpan.FromMinutes(5);

        [Serializable]
        private class TokenResponse
        {
            public string access_token;
            public string id_token;
            public string refresh_token;
            public string token_type;
            public int expires_in;
            public string error;
            public string error_description;
        }

        /// <summary>Runs the full flow end to end: opens the browser, waits for consent, exchanges
        /// the code, and returns the id_token. Throws on any failure - the caller is expected to
        /// catch and surface the message.</summary>
        public static async Task<string> SignInAndGetIdTokenAsync()
        {
            if (string.IsNullOrEmpty(ClientId))
                throw new InvalidOperationException(
                    "Aucun Google Client ID configure - renseigne GameManager > Google Client Id dans l'inspecteur.");

            int port = GetFreeLoopbackPort();
            string redirectUri = $"http://127.0.0.1:{port}/";

            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);
            string state = Guid.NewGuid().ToString("N");

            string authUrl = AuthEndpoint +
                "?client_id=" + Uri.EscapeDataString(ClientId) +
                "&redirect_uri=" + Uri.EscapeDataString(redirectUri) +
                "&response_type=code" +
                "&scope=" + Uri.EscapeDataString(Scope) +
                "&code_challenge=" + codeChallenge +
                "&code_challenge_method=S256" +
                "&state=" + state +
                "&prompt=select_account";

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            try
            {
                Application.OpenURL(authUrl);

                string code = await WaitForRedirectAsync(listener, state);
                return await ExchangeCodeForIdTokenAsync(code, codeVerifier, redirectUri);
            }
            finally
            {
                listener.Stop();
                listener.Close();
            }
        }

        /// <summary>Binds to port 0 to let the OS hand back a free port, then releases it for
        /// HttpListener to bind to. Small theoretical race, but the standard approach.</summary>
        private static int GetFreeLoopbackPort()
        {
            var tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            int port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
            tcpListener.Stop();
            return port;
        }

        private static async Task<string> WaitForRedirectAsync(HttpListener listener, string expectedState)
        {
            // GetContextAsync() has no built-in timeout, so it's raced against a plain delay.
            // If the timeout wins, GetContextAsync is abandoned - the caller's finally block
            // stops/closes the listener, which unblocks it.
            var getContextTask = listener.GetContextAsync();
            var timeoutTask = Task.Delay(RedirectTimeout);
            var finished = await Task.WhenAny(getContextTask, timeoutTask);

            if (finished == timeoutTask)
                throw new TimeoutException(
                    "Pas de reponse de Google apres 5 minutes - connexion annulee, onglet ferme, ou " +
                    "ton compte Google n'est pas dans la liste 'Test users' du projet (verifie l'ecran " +
                    "de consentement OAuth sur Google Cloud Console).");

            var context = getContextTask.Result;
            var request = context.Request;
            var response = context.Response;

            string code = request.QueryString["code"];
            string state = request.QueryString["state"];
            string error = request.QueryString["error"];

            string page = error != null
                ? "<html><body><h2>Connexion annulee.</h2><p>Tu peux fermer cet onglet et revenir au jeu.</p></body></html>"
                : "<html><body><h2>Connexion reussie !</h2><p>Tu peux fermer cet onglet et revenir au jeu.</p></body></html>";

            byte[] buffer = Encoding.UTF8.GetBytes(page);
            response.ContentLength64 = buffer.Length;
            response.ContentType = "text/html; charset=utf-8";
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();

            if (error != null)
                throw new Exception($"Google a renvoye une erreur : {error}");
            if (state != expectedState)
                throw new Exception("Reponse OAuth invalide (state incorrect) - abandon par securite.");
            if (string.IsNullOrEmpty(code))
                throw new Exception("Aucun code d'autorisation recu de Google.");

            return code;
        }

        private static async Task<string> ExchangeCodeForIdTokenAsync(string code, string codeVerifier, string redirectUri)
        {
            var form = new WWWForm();
            form.AddField("client_id", ClientId);
            if (!string.IsNullOrEmpty(ClientSecret)) form.AddField("client_secret", ClientSecret);
            form.AddField("code", code);
            form.AddField("code_verifier", codeVerifier);
            form.AddField("grant_type", "authorization_code");
            form.AddField("redirect_uri", redirectUri);

            using var request = UnityWebRequest.Post(TokenEndpoint, form);
            var op = request.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";

            if (request.result != UnityWebRequest.Result.Success)
                throw new Exception($"Echange du code Google echoue : {request.error} - {responseText}");

            TokenResponse token;
            try { token = JsonUtility.FromJson<TokenResponse>(responseText); }
            catch (Exception e) { throw new Exception($"Reponse Google illisible : {e.Message}"); }

            if (token != null && !string.IsNullOrEmpty(token.error))
                throw new Exception($"Google a refuse l'echange : {token.error} - {token.error_description}");

            if (token == null || string.IsNullOrEmpty(token.id_token))
                throw new Exception($"Reponse Google sans id_token : {responseText}");

            return token.id_token;
        }

        private static string GenerateCodeVerifier()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
