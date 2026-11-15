using System;
using UnityEngine;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace ElementalBuddies
{
    // App Tracking Transparency (iOS 14+, iOS-Plan 2026-11-15): Frage nach der Werbe-ID (IDFA) vor dem Start von LevelPlay,
    // nur mit echtem Werbe-Anbieter (nie im Tester-Build, nie ohne iOS-Schlüssel). Nativ: Assets/Plugins/iOS/EBTracking.mm,
    // Info.plist-Text NSUserTrackingUsageDescription aus IosBuildPostProcess. Außerhalb von iOS: sofort „nicht nötig“.
    public static class IosTracking
    {
        // Werte wie ATTrackingManagerAuthorizationStatus; NotRequired = keine iOS-Abfrage (Editor, Android, PC)
        public enum Status { NotRequired = -1, NotDetermined = 0, Restricted = 1, Denied = 2, Authorized = 3 }

#if UNITY_IOS && !UNITY_EDITOR
        private delegate void StatusCallback(int status);

        [DllImport("__Internal")] private static extern int EBTracking_GetStatus();
        [DllImport("__Internal")] private static extern void EBTracking_Request(StatusCallback callback);

        private static Action<Status> _pending;
        private static bool _requesting;

        public static Status Current => (Status)EBTracking_GetStatus();

        // Systemdialog zeigen, falls noch nicht beantwortet; done kommt auf dem Hauptthread
        public static void Request(Action<Status> done)
        {
            var now = Current;
            if (now != Status.NotDetermined) { done?.Invoke(now); return; }
            _pending += done;
            if (_requesting) return;
            _requesting = true;
            EBTracking_Request(OnStatus);
        }

        [AOT.MonoPInvokeCallback(typeof(StatusCallback))]
        private static void OnStatus(int status)
        {
            _requesting = false;
            var done = _pending;
            _pending = null;
            try { done?.Invoke((Status)status); }
            catch (Exception e) { Debug.LogException(e); }
        }
#else
        public static Status Current => Status.NotRequired;

        public static void Request(Action<Status> done) => done?.Invoke(Status.NotRequired);
#endif
    }
}
