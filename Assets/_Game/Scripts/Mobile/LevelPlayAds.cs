using System;
using System.Collections;
using UnityEngine;
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
using Unity.Services.LevelPlay;
#endif

namespace ElementalBuddies
{
    // Werbe-Anbieter LevelPlay (Unity Ads Mediation, Paket com.unity.services.levelplay; Stufe 3 Werbung-Plan, 2026-11-02).
    // Eine Rewarded-Ad-Unit je Belohnung (Dashboard: LevelPlay → Elemental Buddies → Ad units). Init erst nach der
    // Einwilligung (AdConsent → SetGDPRConsent). Videos werden vorgeladen und nach dem Zeigen bzw. nach einem Fehler neu
    // geladen (mit Pause). Die Belohnung kann laut Doku auch NACH OnAdClosed kommen → kurz warten, bevor „keine Belohnung“
    // gemeldet wird. Testanzeigen: das Gerät ist im Dashboard als Testgerät eingetragen (Lenovo Tab M11).
    // Nur auf Android/iOS-Geräten mit Schlüsseln (iOS: Platzhalter, iOS-Plan 2026-11-15); im Editor und auf dem PC gibt es
    // diesen Anbieter nicht (Create → null).
    public static class LevelPlayAds
    {
        // Android-App „Elemental Buddies“ (LevelPlay-Dashboard)
        public const string AndroidAppKey = "2935f9bed";
        // Reihenfolge wie AdReward (Reroll, BuddySlot, Continue)
        private static readonly string[] AndroidAdUnits = { "3d0kfv7ujb7yd870", "29gbh1649baihitf", "f3f0seskge0xu0da" };
        // iOS-App: eintragen, sobald sie im LevelPlay-Portal angelegt ist; leer = kein Anbieter (wie PC, keine Knöpfe)
        public const string IosAppKey = "";
        private static readonly string[] IosAdUnits = { "", "", "" };
        // nach OnAdClosed so lange auf OnAdRewarded warten (Echtzeit, das Spiel steht dabei)
        private const float RewardGrace = 1.5f;
        private static readonly float[] RetryDelays = { 5f, 15f, 30f, 60f };

        // Schlüssel der laufenden Plattform (Editor/PC: Android-Werte, dort ohne Wirkung)
        private static bool Ios => Application.platform == RuntimePlatform.IPhonePlayer;
        private static string AppKey => Ios ? IosAppKey : AndroidAppKey;
        private static string[] AdUnits => Ios ? IosAdUnits : AndroidAdUnits;
        // App-Key und alle Ad-Units vorhanden?
        public static bool Configured => AdKeyRules.IsConfigured(AppKey, AdUnits);

        public static RewardedAds.IProvider Create()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            return Configured ? new Provider() : null; // iOS: Schlüssel fehlen noch → null
#else
            return null; // Editor/PC: keine Werbung
#endif
        }

        // Start nach der Einwilligung; auf iOS zusätzlich erst nach der ATT-Abfrage (App Tracking Transparency)
        public static void Start(MonoBehaviour host, RewardedAds.IProvider provider, bool consent)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (provider is Provider p) p.Init(host, consent);
#elif UNITY_IOS && !UNITY_EDITOR
            if (provider is Provider p)
                IosTracking.Request(status =>
                {
                    Debug.Log("[LevelPlayAds] ATT: " + status);
                    p.Init(host, consent);
                });
#endif
        }

        // Einwilligung geändert (Datenschutz-Optionen) – gilt für folgende Anzeigen
        public static void ApplyConsent(bool consent)
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (!Configured) return;
            LevelPlayPrivacySettings.SetGDPRConsent(consent);
#endif
        }

        // Development-Build: LevelPlay-Test-Suite (prüft Netzwerke/Adapter auf dem Gerät), Test-Fernsteuerung „adsuite“
        public static bool LaunchTestSuite()
        {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (!Debug.isDebugBuild || !Configured) return false;
            LevelPlay.LaunchTestSuite();
            return true;
#else
            return false;
#endif
        }

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        private sealed class Provider : RewardedAds.IProvider
        {
            private MonoBehaviour _host;
            private LevelPlayRewardedAd[] _ads;
            private readonly int[] _failures = new int[AdUnits.Length];
            private bool _initStarted, _initDone;
            private string _state = "wartet auf Einwilligung";

            // laufende Anzeige
            private int _showing = -1;
            private bool _rewarded, _closed;
            private Action<bool> _done;

            public string Name => _initDone ? "LevelPlay" : "LevelPlay (" + _state + ")";

            public bool IsReady(AdReward r)
            {
                int i = (int)r;
                return _initDone && _ads != null && i < _ads.Length && _ads[i] != null && _ads[i].IsAdReady();
            }

            public void Init(MonoBehaviour host, bool consent)
            {
                if (_initStarted) return;
                _initStarted = true;
                _host = host;
                _state = "startet";
                LevelPlayPrivacySettings.SetGDPRConsent(consent);
                LevelPlayPrivacySettings.SetCOPPA(false); // Dashboard: nicht an Kinder gerichtet
                if (Debug.isDebugBuild) LevelPlay.SetMetaData("is_test_suite", "enable");
                LevelPlay.OnInitSuccess += OnInitSuccess;
                LevelPlay.OnInitFailed += OnInitFailed;
                LevelPlay.Init(AppKey);
            }

            private void OnInitSuccess(LevelPlayConfiguration config)
            {
                _initDone = true;
                Debug.Log("[LevelPlayAds] Init ok – lade Rewarded-Anzeigen");
                _ads = new LevelPlayRewardedAd[AdUnits.Length];
                for (int i = 0; i < _ads.Length; i++)
                {
                    int slot = i;
                    var ad = new LevelPlayRewardedAd(AdUnits[i]);
                    ad.OnAdLoaded += info => { _failures[slot] = 0; Debug.Log($"[LevelPlayAds] {(AdReward)slot} geladen ({info?.AdNetwork})"); };
                    ad.OnAdLoadFailed += err => OnLoadFailed(slot, err);
                    ad.OnAdRewarded += (info, reward) => { if (_showing == slot) _rewarded = true; };
                    ad.OnAdDisplayFailed += (info, err) =>
                    {
                        Debug.LogWarning($"[LevelPlayAds] {(AdReward)slot} Anzeige fehlgeschlagen: {err}");
                        if (_showing == slot) Finish();
                        ad.LoadAd();
                    };
                    ad.OnAdClosed += info =>
                    {
                        if (_showing != slot) return;
                        _closed = true;
                        _host.StartCoroutine(WaitForReward());
                        ad.LoadAd();
                    };
                    _ads[i] = ad;
                    ad.LoadAd();
                }
            }

            private void OnInitFailed(LevelPlayInitError err)
            {
                _state = "Init fehlgeschlagen";
                Debug.LogWarning("[LevelPlayAds] Init fehlgeschlagen: " + err);
            }

            private void OnLoadFailed(int slot, LevelPlayAdError err)
            {
                int n = _failures[slot]++;
                float delay = RetryDelays[Mathf.Min(n, RetryDelays.Length - 1)];
                Debug.Log($"[LevelPlayAds] {(AdReward)slot} nicht geladen ({err?.ErrorCode}: {err?.ErrorMessage}) – neuer Versuch in {delay:0} s");
                _host.StartCoroutine(Reload(slot, delay));
            }

            private IEnumerator Reload(int slot, float delay)
            {
                yield return new WaitForSecondsRealtime(delay);
                if (_ads != null && _ads[slot] != null && !_ads[slot].IsAdReady()) _ads[slot].LoadAd();
            }

            public void Show(AdReward r, Action<bool> done)
            {
                int i = (int)r;
                if (!IsReady(r)) { done(false); return; }
                _showing = i;
                _rewarded = false;
                _closed = false;
                _done = done;
                _ads[i].ShowAd();
            }

            private IEnumerator WaitForReward()
            {
                float end = Time.realtimeSinceStartup + RewardGrace;
                while (!_rewarded && Time.realtimeSinceStartup < end) yield return null;
                Finish();
            }

            private void Finish()
            {
                if (_done == null) return;
                var done = _done;
                bool ok = _rewarded;
                _done = null;
                _showing = -1;
                done(ok);
            }
        }
#endif
    }
}
