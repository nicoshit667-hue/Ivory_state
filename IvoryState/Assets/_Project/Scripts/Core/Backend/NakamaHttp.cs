using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace IvoryState.Core.Backend
{
    /// <summary>
    /// Client HTTP minimal pour l'API REST de Nakama : pas de SDK, seulement les appels utiles.
    /// <list type="bullet">
    /// <item><see cref="AuthenticateDevice"/> : le téléphone obtient un jeton de session (clé serveur publique).</item>
    /// <item><see cref="GetUserId"/> : le serveur de jeu vérifie un jeton reçu d'un client.</item>
    /// <item><see cref="Rpc"/> : le serveur de jeu appelle les RPC réservées (clé http secrète).</item>
    /// </list>
    /// Toujours sur le thread principal (UnityWebRequest), en async/await, avec nouvelle tentative
    /// automatique si le conteneur Docker/Postgres sort de veille (timeout initial).
    /// </summary>
    public static class NakamaHttp
    {
        private const int TimeoutSeconds = 20;
        private const int MaxAttempts = 2;

        public readonly struct Result
        {
            public readonly bool Ok;
            public readonly long Status;
            public readonly string Body;
            public readonly string Error;

            public Result(bool ok, long status, string body, string error)
            {
                Ok = ok;
                Status = status;
                Body = body;
                Error = error;
            }
        }

        [Serializable] private class DeviceRequest { public string id; }
        [Serializable] private class SessionResponse { public string token; }
        [Serializable] private class AccountResponse { public AccountUser user; }
        [Serializable] private class AccountUser { public string id; public string username; }

        [Serializable] private class EmailRequest { public string email; public string password; }

        /// <summary>
        /// Authentification par e-mail et mot de passe. <paramref name="create"/> : création du compte
        /// (avec le pseudo <paramref name="username"/>, unique dans Nakama). Renvoie le résultat HTTP brut
        /// (409 : pseudo déjà pris ; 401 : mot de passe incorrect ; 404 : compte inconnu) et le jeton.
        /// </summary>
        public static async Awaitable<(Result result, string token)> AuthenticateEmail(string baseUrl, string serverKey,
            string email, string password, bool create, string username)
        {
            string url = baseUrl + "/v2/account/authenticate/email?create=" + (create ? "true" : "false");
            if (create && !string.IsNullOrEmpty(username))
                url += "&username=" + UnityWebRequest.EscapeURL(username);
            string body = JsonUtility.ToJson(new EmailRequest { email = email, password = password });
            string basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(serverKey + ":"));

            Result result = await Send(UnityWebRequest.kHttpVerbPOST, url, body, basic);
            string token = result.Ok ? JsonUtility.FromJson<SessionResponse>(result.Body)?.token : null;
            return (result, token);
        }

        /// <summary>Authentification par identifiant d'appareil (compte créé au premier appel).</summary>
        public static async Awaitable<string> AuthenticateDevice(string baseUrl, string serverKey, string deviceId)
        {
            string url = baseUrl + "/v2/account/authenticate/device?create=true";
            string body = JsonUtility.ToJson(new DeviceRequest { id = deviceId });
            string basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(serverKey + ":"));

            Result result = await Send(UnityWebRequest.kHttpVerbPOST, url, body, basic);
            return result.Ok ? JsonUtility.FromJson<SessionResponse>(result.Body)?.token : null;
        }

        /// <summary>Vérifie un jeton client et renvoie l'identifiant du compte (null si invalide).</summary>
        public static async Awaitable<string> GetUserId(string baseUrl, string sessionToken)
        {
            Result result = await Send(UnityWebRequest.kHttpVerbGET, baseUrl + "/v2/account", null, "Bearer " + sessionToken);
            return result.Ok ? JsonUtility.FromJson<AccountResponse>(result.Body)?.user?.id : null;
        }

        /// <summary>RPC serveur à serveur (clé http secrète, charge utile JSON brute).</summary>
        public static Awaitable<Result> Rpc(string baseUrl, string httpKey, string rpcId, string jsonPayload)
        {
            string url = baseUrl + "/v2/rpc/" + rpcId + "?unwrap&http_key=" + UnityWebRequest.EscapeURL(httpKey);
            return Send(UnityWebRequest.kHttpVerbPOST, url, jsonPayload, null);
        }

        private static async Awaitable<Result> Send(string method, string url, string body, string authorization)
        {
            Result last = default;
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                using var request = new UnityWebRequest(url, method)
                {
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = TimeoutSeconds,
                };

                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }

                if (authorization != null)
                    request.SetRequestHeader("Authorization", authorization);

                await request.SendWebRequest();

                bool ok = request.result == UnityWebRequest.Result.Success;
                last = new Result(ok, request.responseCode, request.downloadHandler.text, ok ? null : request.error);
                if (ok || request.responseCode != 0)
                    return last;
            }
            return last;
        }
    }
}
