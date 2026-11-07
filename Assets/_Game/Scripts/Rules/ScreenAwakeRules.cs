namespace ElementalBuddies
{
    // Bildschirm wach halten (Koop-Absturz-Fix 2026-11-02). Auf Handys schaltet sich der Bildschirm nach ~30 s ohne
    // Berührung aus (z. B. beim Warten auf die Kartenwahl des Mitspielers). Android pausiert dann die App; beim Host
    // läuft nach rund einer Minute die Relay-Verbindung ab und ALLE fliegen raus. Gemessen auf dem Tablet: 20 s aus
    // übersteht der Koop, 60 s nicht („Verbindung zum Relay-Server verloren“).
    public static class ScreenAwakeRules
    {
        // ab dieser Pausendauer wird ein Verbindungsverlust der Pause zugeschrieben
        public const float PauseExplainsLossSeconds = 8f;
        // … wenn der Verlust so kurz nach dem Zurückkehren gemeldet wird
        public const float LossAfterResumeWindow = 10f;

        // Nur Touch-Geräte; im Spiel immer (auch solo – sonst wird es mitten in der Welle dunkel), im Menü nur bei
        // offenem Online-Raum (Lobby: der Host wartet dort oft ohne Berührung auf Mitspieler)
        public static bool KeepAwake(bool touchMode, bool inGameScene, bool onlineSession)
            => touchMode && (inGameScene || onlineSession);

        // War die App gerade im Hintergrund und lange genug, um die Verbindung zu verlieren?
        public static bool PauseExplainsLoss(float pausedSeconds, float secondsSinceResume)
            => pausedSeconds >= PauseExplainsLossSeconds && secondsSinceResume >= 0f && secondsSinceResume <= LossAfterResumeWindow;

        public const string PauseLossMessage =
            "Die Verbindung ist abgelaufen, weil das Spiel im Hintergrund war (Bildschirm aus oder App gewechselt).";
    }
}
