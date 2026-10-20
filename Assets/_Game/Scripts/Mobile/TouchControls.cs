using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ElementalBuddies
{
    // Zielvorgabe der Touch-Steuerung für PlayerController.ApplyTouchAim (ersetzt im Touch-Modus den Maus-Raycast)
    public enum TouchAimMode
    {
        Keep,      // Blickrichtung behalten (kurz nach einem Angriff / ohne Ziel)
        FaceMove,  // in Laufrichtung drehen
        Direction, // in Richtung drehen (Angriffs-Stick, Richtungs-Fähigkeiten)
        Point      // zum Bodenpunkt drehen, ctx.AimPoint = Punkt (Ziel-Fähigkeiten, Auto-Ziel)
    }

    public struct TouchAim
    {
        public TouchAimMode Mode;
        public Vector3 Direction; // Welt-XZ, normiert (Direction)
        public float Distance;    // Direction: Entfernung des Zielpunkts vor der Figur
        public Vector3 Point;     // Point: Bodenpunkt
        public bool HasPoint;     // wird zu ctx.HasAimPoint

        public static TouchAim Keep => new TouchAim { Mode = TouchAimMode.Keep };
        public static TouchAim FaceMove => new TouchAim { Mode = TouchAimMode.FaceMove };
    }

    // Touch-Steuerung der eigenen Figur (Mobil-Version, Paket S). Nur im Touch-Modus vorhanden: PlayerController legt
    // sie beim Einschalten der Eingabe an (Bind) – auf dem PC existiert die Komponente gar nicht.
    //
    // - Bewegungs-Stick links unten (schwebend): Move → PlayerController.MoveInput/MoveWorld (kamerabezogen wie WASD)
    // - Angriffs-Stick rechts unten: Ziehen = zielen + Primärangriff halten, kurz Tippen = Angriff aufs nächste Ziel,
    //   ruhig Halten = Dauerfeuer aufs nächste Ziel
    // - Fähigkeits-Knöpfe (RMB-Slot, R/F/C/V) im Bogen um den Angriffs-Stick: Tippen = auslösen (Auto-Ziel),
    //   Ziehen = zielen mit Bodenvorschau, Loslassen = auslösen, zurück auf den Knopf = abbrechen; Halte-Fähigkeiten halten
    // - Sprung-Knopf im selben Bogen
    // - Zwei Finger in der Welt-Zone drehen = Kamera drehen, Doppeltipp mit zwei Fingern = zurücksetzen
    //
    // Ausgaben gelten für den aktuellen Frame (läuft vor PlayerController/PlayerAbilities/CameraFollow):
    // SlotPressed/SlotHeld (PlayerAbilities), JumpPressed + Move + CurrentAim (PlayerController), CameraYawDelta/
    // CameraResetPressed (CameraFollow). Finger werden über TouchZones beansprucht (Bauen/UI laufen parallel).
    [DefaultExecutionOrder(-100)]
    public class TouchControls : MonoBehaviour
    {
        public static TouchControls Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        // ---------------- Layout (dp) ----------------
        public const float StickRadiusDp = 64f;      // voller Ausschlag der Sticks
        public const float KnobRadiusDp = 30f;
        public const float MoveInsetDp = 124f;       // Ruheposition Bewegungs-Stick (Abstand vom Rand links/unten)
        public const float AttackInsetDp = 104f;     // Mitte Angriffs-Stick (Abstand vom Rand rechts/unten)
        public const float ButtonDiameterDp = 56f;   // Fähigkeits-/Sprung-Knöpfe (≥ MobilePlatform.MinTouchTargetDp)
        public const float ArcRadiusDp = 125f;       // Bogen der Knöpfe um den Angriffs-Stick
        public const float JumpAngle = 205f;         // Grad (0 = rechts, 90 = oben)
        public static readonly float[] ButtonAngles = { 178f, 151f, 124f, 97f, 70f };
        public static readonly AbilitySlot[] ButtonSlots = { AbilitySlot.Secondary, AbilitySlot.Fire, AbilitySlot.Ice, AbilitySlot.Earth, AbilitySlot.Light };
        public const int ButtonCount = 5;
        public const float DragFullDp = 150f;        // Zieh-Weg (ab Knopfrand) für volle Reichweite

        // ---------------- Verhalten ----------------
        public const float MoveDeadzone = 0.12f;
        public const float AttackDeadzone = 0.28f;
        public const float TapMaxTime = 0.25f;       // s; länger ruhig gehalten = Dauerfeuer aufs nächste Ziel
        public const float AimHoldTime = 0.4f;       // s; so lange nach einem Angriff bleibt die Blickrichtung stehen
        public const float DragMinFraction = 0.12f;  // kürzester Zieh-Abstand = 12 % der Reichweite
        public const float AutoAimMin = 6f, AutoAimMax = 16f, AutoAimFallback = 10f;
        public const float FacingWeight = 0.6f;
        public const float TwistStartDeg = 6f;
        public const float TwistMinSpanDp = 40f;
        public const float TwistSensitivity = 1f;
        public const float TwoFingerTapTime = 0.3f;
        public const float TwoFingerTapTravelDp = 20f;
        public const float TwoFingerStartGap = 0.2f; // s zwischen den beiden Fingern eines Zwei-Finger-Tipps
        public const float DoubleTapWindow = 0.45f;

        private const int MaxFingers = 16;

        // ---------------- Bindung ----------------
        private PlayerController _player;
        private PlayerAbilities _abilities;
        private PlayerStats _stats;
        private TouchControlsView _view;

        public PlayerController Player => _player;
        public PlayerAbilities Abilities => _abilities;
        public bool IsBoundTo(PlayerController pc) => pc != null && _player == pc;

        // Von PlayerController.SetInputActive(true) (nur im Touch-Modus)
        public static void Bind(PlayerController pc)
        {
            if (pc == null || !MobilePlatform.TouchMode) return;
            if (Instance == null)
            {
                var go = new GameObject("TouchControls");
                go.AddComponent<TouchControls>(); // Awake setzt Instance
            }
            if (Instance != null) Instance.SetPlayer(pc);
        }

        public static void Unbind(PlayerController pc)
        {
            if (Instance != null && Instance._player == pc) Instance.SetPlayer(null);
        }

        private void SetPlayer(PlayerController pc)
        {
            if (_player == pc) return;
            ResetAll(true);
            _player = pc;
            _abilities = pc != null ? pc.GetComponent<PlayerAbilities>() : null;
            _stats = pc != null ? pc.GetComponent<PlayerStats>() : null;
            _primaryId = (AbilityId)(-1);
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            TouchZones.EnsureEnabled();
            for (int i = 0; i < ButtonCount; i++) _buttons[i] = new ButtonState();
            for (int i = 0; i < ButtonCount; i++) ButtonCenters[i] = Vector2.zero;
            _view = gameObject.AddComponent<TouchControlsView>();
            gameObject.AddComponent<TouchAimPreview>();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            ResetAll(false);
            Instance = null;
        }

        // ---------------- Frame-Ausgaben ----------------
        private readonly bool[] _slotPressed = new bool[AbilitySlots.Count];
        private readonly bool[] _slotHeld = new bool[AbilitySlots.Count];
        private bool _jumpPressed;
        private float _yawDelta;
        private bool _cameraReset;
        private TouchAim _aim = TouchAim.FaceMove;
        private Vector2 _move;

        public Vector2 Move => _move;
        public bool JumpPressed => _jumpPressed;
        public bool SlotPressed(AbilitySlot slot) => _slotPressed[(int)slot];
        public bool SlotHeld(AbilitySlot slot) => _slotHeld[(int)slot];
        public TouchAim CurrentAim => _aim;
        public float CameraYawDelta => _yawDelta;
        public bool CameraResetPressed => _cameraReset;

        // Sichtbarkeit für die Anzeige (unter Menüs/tot/ohne Figur aus, beim Platzieren gedimmt)
        public bool ControlsVisible { get; private set; }
        public bool Dimmed { get; private set; }

        // ---------------- Layout (px, vom sicheren Bereich abgeleitet) ----------------
        public Vector2 MoveRest { get; private set; }
        public Vector2 AttackCenter { get; private set; }
        public Vector2 JumpCenter { get; private set; }
        public readonly Vector2[] ButtonCenters = new Vector2[ButtonCount];
        public float StickRadius { get; private set; }
        public float KnobRadius { get; private set; }
        public float ButtonRadius { get; private set; }
        public float HitRadius { get; private set; }
        public int LayoutVersion { get; private set; }
        private float DragStartPx => ButtonRadius * 1.1f;
        private float CancelRadiusPx => ButtonRadius;
        private float DragFullPx => ButtonRadius + DragFullDp * _pxPerDp;
        private float _pxPerDp = 1f;
        private Rect _layoutSafe;
        private Vector2Int _layoutScreen;
        private float _layoutDpi;

        // ---------------- Finger ----------------
        private struct FingerData
        {
            public bool Present;
            public bool Ended;
            public Vector2 Pos;
        }

        private readonly FingerData[] _fingers = new FingerData[MaxFingers];
        private readonly bool[] _ignored = new bool[MaxFingers]; // gehört nicht uns (UI, belegt, Menü) bis zum Loslassen

        private bool Alive(int f) => f >= 0 && f < MaxFingers && _fingers[f].Present && !_fingers[f].Ended;
        private Vector2 PosOf(int f) => _fingers[f].Pos;

        // Bewegungs-Stick
        private int _moveFinger = -1;
        private Vector2 _moveOrigin, _movePos;
        public bool MoveActive => _moveFinger >= 0;
        public Vector2 MoveOrigin => _moveOrigin;
        public Vector2 MoveKnob => _moveOrigin + Vector2.ClampMagnitude(_movePos - _moveOrigin, StickRadius);

        // Angriffs-Stick
        private int _atkFinger = -1;
        private Vector2 _atkOrigin, _atkPos;
        private float _atkStartTime;
        private bool _atkFiring, _atkAuto;
        private Vector3 _atkDir = Vector3.forward;
        public bool AttackActive => _atkFinger >= 0;
        public bool AttackFiring => _atkFiring;
        public Vector2 AttackOrigin => _atkOrigin;
        public Vector2 AttackKnob => _atkOrigin + Vector2.ClampMagnitude(_atkPos - _atkOrigin, StickRadius);

        // Fähigkeits-Knöpfe
        private class ButtonState
        {
            public int Finger = -1;
            public AbilityId Id;
            public bool Hold;
            public TouchAimKind Kind;
            public bool Dragged, Cancel, Aiming;
            public float StartTime;
            public bool Info; // langes Drücken ohne Ziehen → Tooltip; Loslassen löst dann nicht aus
            public Vector3 AimDir;
            public float AimDist, Range, Area;
        }

        private readonly ButtonState[] _buttons = new ButtonState[ButtonCount];
        public bool ButtonDown(int i) => _buttons[i].Finger >= 0;
        public bool ButtonCancel(int i) => _buttons[i].Finger >= 0 && _buttons[i].Dragged && _buttons[i].Cancel;
        public bool ButtonAiming(int i) => _buttons[i].Finger >= 0 && _buttons[i].Aiming && !_buttons[i].Cancel;

        // Sprung
        private int _jumpFinger = -1;
        public bool JumpDown => _jumpFinger >= 0;

        // Kamera (zwei Finger in der Welt-Zone)
        private int _camA = -1, _camB = -1;
        private Vector2 _camPrevA, _camPrevB, _camStartA, _camStartB;
        private float _camTimeA, _camTimeB, _camTravel;
        private bool _twisting, _pairValid;
        private float _twistAccum;
        private float _lastTwoTapTime = float.NegativeInfinity;

        // Ziel
        private bool _frameAimSet;
        private TouchAim _frameAim;
        private float _lastAimTime = float.NegativeInfinity;
        private AbilityId _primaryId = (AbilityId)(-1);
        private float _primaryRange = 10f;
        private EnemyBrain _autoTarget;
        private float _autoPickAt;

        // ---------------- Update ----------------

        void Update()
        {
            System.Array.Clear(_slotPressed, 0, _slotPressed.Length);
            System.Array.Clear(_slotHeld, 0, _slotHeld.Length);
            _jumpPressed = false;
            _yawDelta = 0f;
            _cameraReset = false;
            _frameAimSet = false;

            CollectFingers();
            UpdateIgnored();

            bool allowed = InputAllowed();
            ControlsVisible = allowed;
            if (!allowed)
            {
                ResetAll(true);
                _aim = TouchAim.Keep;
                return;
            }
            Dimmed = InteractionManager.Instance != null && InteractionManager.Instance.SelectedIndex >= 0;

            UpdateLayout();
            UpdateMove();
            UpdateAttack();
            UpdateButtons();
            UpdateJump();
            BeginNewFingers();
            UpdateCamera();
            ResolveAim();
        }

        private void CollectFingers()
        {
            for (int i = 0; i < MaxFingers; i++) _fingers[i].Present = false;
            foreach (var t in ETouch.activeTouches)
            {
                int f = t.finger.index;
                if (f < 0 || f >= MaxFingers) continue;
                var ph = t.phase;
                _fingers[f].Present = true;
                _fingers[f].Pos = t.screenPosition;
                _fingers[f].Ended = ph == ETouchPhase.Ended || ph == ETouchPhase.Canceled;
            }
        }

        private void UpdateIgnored()
        {
            for (int f = 0; f < MaxFingers; f++)
            {
                if (!_ignored[f] || Alive(f)) continue;
                _ignored[f] = false;
                if (TouchZones.OwnerOf(f) == TouchZones.Owner.Camera) TouchZones.Release(f);
            }
        }

        // ---------------- Freigabe / Menüs ----------------

        private bool InputAllowed()
        {
            if (_player == null || !_player.isActiveAndEnabled || !_player.IsLocalControl) return false;
            if (_stats != null && _stats.IsDead) return false;
            if (InteractionManager.LocalInputBlocked) return false; // Pause, Kartenwahl, Händler, Solo-Zeitstopp
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.GameOver) return false;
            return !IsModalOpen();
        }

        private UpgradeScreenUI _upgradeUi;
        private MerchantScreenUI _merchantUi;
        private GameOverUI _gameOverUi;
        private float _nextModalLookup;

        // Wie OffscreenIndicatorUI: Pause, Kartenwahl, Händler, Game Over/Morgengrauen, Tastenbelegung
        private bool IsModalOpen()
        {
            if (PauseManager.IsPaused || KeyBindingsMenu.IsOpen) return true;
            var pause = PauseMenuUI.Active;
            if (pause != null && pause.Root != null && pause.Root.activeInHierarchy) return true;
            if ((_upgradeUi == null || _merchantUi == null || _gameOverUi == null) && Time.unscaledTime >= _nextModalLookup)
            {
                _nextModalLookup = Time.unscaledTime + 2f;
                if (_upgradeUi == null) _upgradeUi = FindFirstObjectByType<UpgradeScreenUI>(FindObjectsInactive.Include);
                if (_merchantUi == null) _merchantUi = FindFirstObjectByType<MerchantScreenUI>(FindObjectsInactive.Include);
                if (_gameOverUi == null) _gameOverUi = FindFirstObjectByType<GameOverUI>(FindObjectsInactive.Include);
            }
            if (_upgradeUi != null && _upgradeUi.Panel != null && _upgradeUi.Panel.activeInHierarchy) return true;
            if (_merchantUi != null && _merchantUi.Panel != null && _merchantUi.Panel.activeInHierarchy) return true;
            if (_gameOverUi != null && _gameOverUi.Panel != null && _gameOverUi.Panel.activeInHierarchy) return true;
            return false;
        }

        // Alles loslassen (Menü, Tod, Figurwechsel). markIgnored: noch liegende Finger bleiben bis zum Loslassen wirkungslos,
        // damit nach dem Schließen eines Menüs kein Stick/Knopf „von selbst“ weiterläuft.
        private void ResetAll(bool markIgnored)
        {
            ReleaseMine(_moveFinger);
            ReleaseMine(_atkFinger);
            ReleaseMine(_jumpFinger);
            ReleaseMine(_camA);
            ReleaseMine(_camB);
            for (int i = 0; i < ButtonCount; i++)
            {
                if (_buttons[i] == null) continue;
                ReleaseMine(_buttons[i].Finger);
                ResetButton(_buttons[i]);
            }
            _moveFinger = _atkFinger = _jumpFinger = _camA = _camB = -1;
            _move = Vector2.zero;
            _atkFiring = _atkAuto = false;
            _twisting = _pairValid = false;
            _twistAccum = 0f;
            _autoTarget = null;
            if (markIgnored)
                for (int f = 0; f < MaxFingers; f++)
                    if (Alive(f)) _ignored[f] = true;
        }

        private static void ResetButton(ButtonState b)
        {
            if (b.Info) HideButtonInfo(b);
            b.Finger = -1;
            b.Dragged = b.Cancel = b.Aiming = false;
        }

        private void ShowButtonInfo(int i, ButtonState b)
        {
            var tip = AbilityTooltipUI.Instance;
            var anchor = _view != null ? _view.ButtonRoot(i) : null;
            if (tip == null || anchor == null) return;
            b.Info = true;
            tip.ShowFor(b.Id, anchor);
        }

        private static void HideButtonInfo(ButtonState b)
        {
            b.Info = false;
            if (AbilityTooltipUI.Instance != null) AbilityTooltipUI.Instance.HideExternal();
        }

        // Nur eigene Ansprüche freigeben (Bauen/UI nicht anfassen)
        private static void ReleaseMine(int f)
        {
            if (f < 0) return;
            var o = TouchZones.OwnerOf(f);
            if (o == TouchZones.Owner.MoveStick || o == TouchZones.Owner.AttackStick || o == TouchZones.Owner.AbilityButton || o == TouchZones.Owner.Camera)
                TouchZones.Release(f);
        }

        // ---------------- Layout ----------------

        private void UpdateLayout()
        {
            Rect safe = MobilePlatform.SafeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            float dpi = MobilePlatform.Dpi;
            if (LayoutVersion > 0 && safe == _layoutSafe && screen == _layoutScreen && Mathf.Approximately(dpi, _layoutDpi)) return;
            _layoutSafe = safe;
            _layoutScreen = screen;
            _layoutDpi = dpi;

            _pxPerDp = MobilePlatform.DpToPixels(1f);
            float r = ButtonDiameterDp * 0.5f;

            // Ausdehnung des Angriffs-Clusters (dp, gemessen von der Ecke rechts unten) → in die Angriffs-Zone einpassen
            Vector2 corner = new Vector2(-AttackInsetDp, AttackInsetDp);
            float w = AttackInsetDp + StickRadiusDp, h = AttackInsetDp + StickRadiusDp;
            for (int i = 0; i <= ButtonCount; i++)
            {
                float a = i < ButtonCount ? ButtonAngles[i] : JumpAngle;
                Vector2 p = TouchControlMath.ArcPoint(corner, ArcRadiusDp, a);
                w = Mathf.Max(w, -p.x + r);
                h = Mathf.Max(h, p.y + r);
            }
            Vector2 zone = new Vector2(safe.width * TouchZones.AttackZoneWidth, safe.height * TouchZones.AttackZoneHeight) / _pxPerDp;
            float scale = TouchControlMath.FitScale(new Vector2(w, h), zone, MobilePlatform.MinTouchTargetDp / ButtonDiameterDp);
            float k = _pxPerDp * scale;

            AttackCenter = new Vector2(safe.xMax - AttackInsetDp * k, safe.yMin + AttackInsetDp * k);
            for (int i = 0; i < ButtonCount; i++) ButtonCenters[i] = TouchControlMath.ArcPoint(AttackCenter, ArcRadiusDp * k, ButtonAngles[i]);
            JumpCenter = TouchControlMath.ArcPoint(AttackCenter, ArcRadiusDp * k, JumpAngle);
            ButtonRadius = r * k;
            float spacing = TouchControlMath.ArcSpacing(ArcRadiusDp * k, ButtonAngles[0] - ButtonAngles[1]);
            HitRadius = Mathf.Min(ButtonRadius * 1.2f, spacing * 0.5f);
            StickRadius = StickRadiusDp * k;
            KnobRadius = KnobRadiusDp * k;
            MoveRest = new Vector2(safe.xMin + MoveInsetDp * k, safe.yMin + MoveInsetDp * k);
            LayoutVersion++;
        }

        // ---------------- Neue Finger ----------------

        private bool IsMine(int f)
        {
            if (f == _moveFinger || f == _atkFinger || f == _jumpFinger || f == _camA || f == _camB) return true;
            for (int i = 0; i < ButtonCount; i++) if (_buttons[i].Finger == f) return true;
            return false;
        }

        private void BeginNewFingers()
        {
            for (int f = 0; f < MaxFingers; f++)
            {
                if (!Alive(f) || _ignored[f] || IsMine(f)) continue;
                Vector2 p = PosOf(f);
                // Schon von einem anderen System übernommen? Einzelne Welt-Finger gehören dem Bauen (Owner.Build:
                // Tippen/Halten/Geist ziehen) – sie bleiben Kamera-Kandidaten: kommt ein zweiter Welt-Finger dazu, gibt
                // das Bauen seinen Finger frei und die Kamera übernimmt beide (erst beim Drehen beansprucht).
                var owner = TouchZones.OwnerOf(f);
                if (owner == TouchZones.Owner.Build && TouchZones.InWorldZone(p))
                {
                    AddCameraCandidate(f, p);
                    continue;
                }
                if (owner != TouchZones.Owner.None)
                {
                    _ignored[f] = true;
                    continue;
                }
                if (OverUI(p))
                {
                    _ignored[f] = true;
                    continue;
                }
                if (TryBeginButton(f, p)) continue;
                if (_jumpFinger < 0 && TouchControlMath.InCircle(p, JumpCenter, HitRadius))
                {
                    _jumpFinger = f;
                    _jumpPressed = true;
                    TouchZones.Claim(f, TouchZones.Owner.AbilityButton);
                    continue;
                }
                if (TouchZones.InAttackZone(p))
                {
                    if (_atkFinger < 0) BeginAttack(f, p);
                    else _ignored[f] = true;
                    continue;
                }
                if (TouchZones.InMoveZone(p))
                {
                    if (_moveFinger < 0) BeginMove(f, p);
                    else _ignored[f] = true;
                    continue;
                }
                AddCameraCandidate(f, p);
            }
        }

        private PointerEventData _ped;
        private EventSystem _pedSystem;
        private readonly List<RaycastResult> _uiHits = new List<RaycastResult>();

        // Beginnt der Finger auf einem UI-Element (HUD, Fenster, Bau-Leiste …), gehört er der UI.
        // Die eigenen Touch-Elemente haben keinen GraphicRaycaster und zählen nicht.
        private bool OverUI(Vector2 screenPos)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            if (_ped == null || _pedSystem != es)
            {
                _ped = new PointerEventData(es);
                _pedSystem = es;
            }
            _ped.position = screenPos;
            _uiHits.Clear();
            es.RaycastAll(_ped, _uiHits);
            bool hit = _uiHits.Count > 0;
            _uiHits.Clear();
            return hit;
        }

        // ---------------- Bewegungs-Stick ----------------

        private void BeginMove(int f, Vector2 p)
        {
            _moveFinger = f;
            TouchZones.Claim(f, TouchZones.Owner.MoveStick);
            // Schwebend: Mitte dort, wo der Daumen aufsetzt – aber so, dass der Ring im sicheren Bereich bleibt
            Rect s = _layoutSafe;
            float m = StickRadius * 0.75f;
            _moveOrigin = new Vector2(Mathf.Clamp(p.x, s.xMin + m, Mathf.Max(s.xMin + m, s.xMax - m)),
                                      Mathf.Clamp(p.y, s.yMin + m, Mathf.Max(s.yMin + m, s.yMax - m)));
            _movePos = p;
            _move = TouchControlMath.StickValue(_moveOrigin, _movePos, StickRadius, MoveDeadzone);
        }

        private void UpdateMove()
        {
            if (_moveFinger < 0) return;
            if (!Alive(_moveFinger))
            {
                ReleaseMine(_moveFinger);
                _moveFinger = -1;
                _move = Vector2.zero;
                return;
            }
            _movePos = PosOf(_moveFinger);
            _moveOrigin = TouchControlMath.FollowOrigin(_moveOrigin, _movePos, StickRadius);
            _move = TouchControlMath.StickValue(_moveOrigin, _movePos, StickRadius, MoveDeadzone);
        }

        // ---------------- Angriffs-Stick ----------------

        private void BeginAttack(int f, Vector2 p)
        {
            _atkFinger = f;
            TouchZones.Claim(f, TouchZones.Owner.AttackStick);
            _atkOrigin = p;
            _atkPos = p;
            _atkStartTime = Time.unscaledTime;
            _atkFiring = false;
            _atkAuto = false;
        }

        private void UpdateAttack()
        {
            if (_atkFinger < 0) return;
            AbilitySlot primary = AbilitySlot.Primary;
            if (!Alive(_atkFinger))
            {
                // Losgelassen, bevor gezielt/gefeuert wurde = Antippen → ein Angriff aufs nächste Ziel
                if (!_atkFiring)
                {
                    _slotPressed[(int)primary] = true;
                    SetFrameAim(AutoAim(PrimaryRange(), false));
                }
                ReleaseMine(_atkFinger);
                _atkFinger = -1;
                _atkFiring = _atkAuto = false;
                _autoTarget = null;
                return;
            }

            _atkPos = PosOf(_atkFinger);
            _atkOrigin = TouchControlMath.FollowOrigin(_atkOrigin, _atkPos, StickRadius);
            Vector2 v = TouchControlMath.StickValue(_atkOrigin, _atkPos, StickRadius, AttackDeadzone);
            if (v != Vector2.zero)
            {
                Vector3 dir = PlayerController.ToWorldDirection(v.normalized);
                if (dir.sqrMagnitude > 1e-6f) _atkDir = dir.normalized;
                _atkAuto = false;
                if (!_atkFiring)
                {
                    _atkFiring = true;
                    _slotPressed[(int)primary] = true;
                }
            }
            else if (!_atkFiring && Time.unscaledTime - _atkStartTime >= TapMaxTime)
            {
                // Ruhig gehalten → Dauerfeuer aufs nächste Ziel
                _atkFiring = true;
                _atkAuto = true;
                _slotPressed[(int)primary] = true;
            }
            if (_atkFiring) _slotHeld[(int)primary] = true;
        }

        private float PrimaryRange()
        {
            if (_abilities == null) return AutoAimFallback;
            AbilityId id = _abilities.GetAbility(AbilitySlot.Primary);
            if (id != _primaryId)
            {
                _primaryId = id;
                _primaryRange = TouchAbilityInfo.RangeOf(_abilities, id);
            }
            return _primaryRange;
        }

        // ---------------- Fähigkeits-Knöpfe ----------------

        private bool TryBeginButton(int f, Vector2 p)
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                var b = _buttons[i];
                if (b.Finger >= 0 || !TouchControlMath.InCircle(p, ButtonCenters[i], HitRadius)) continue;
                AbilitySlot slot = ButtonSlots[i];
                ResetButton(b);
                b.Finger = f;
                b.StartTime = Time.unscaledTime;
                TouchZones.Claim(f, TouchZones.Owner.AbilityButton);
                var kit = _abilities != null ? _abilities.ActiveKit : null;
                b.Id = _abilities != null ? _abilities.GetAbility(slot) : (AbilityId)(int)slot;
                b.Hold = kit != null && kit.IsHoldAbility(b.Id);
                b.Kind = b.Hold ? TouchAimKind.None : TouchAbilityInfo.KindOf(b.Id);
                // Reichweiten einmal pro Druck (Kits erzeugen dafür Kopien mit Händlerkarten)
                b.Range = b.Kind != TouchAimKind.None ? TouchAbilityInfo.RangeOf(_abilities, b.Id) : 0f;
                b.Area = b.Kind == TouchAimKind.Point ? TouchAbilityInfo.AreaRadiusOf(_abilities, b.Id) : 0f;
                // Halten (Schildblock) und Fähigkeiten ohne Ziel lösen sofort aus
                if (b.Hold || b.Kind == TouchAimKind.None)
                {
                    _slotPressed[(int)slot] = true;
                    if (b.Hold) _slotHeld[(int)slot] = true;
                }
                return true;
            }
            return false;
        }

        private void UpdateButtons()
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                var b = _buttons[i];
                if (b.Finger < 0) continue;
                AbilitySlot slot = ButtonSlots[i];
                if (!Alive(b.Finger))
                {
                    EndButton(i, slot);
                    continue;
                }
                if (b.Hold)
                {
                    _slotHeld[(int)slot] = true;
                    continue;
                }

                Vector2 off = PosOf(b.Finger) - ButtonCenters[i];
                if (!b.Dragged && TouchControlMath.IsDragging(off, DragStartPx))
                {
                    b.Dragged = b.Kind != TouchAimKind.None;
                    if (b.Info) HideButtonInfo(b); // doch gezielt → Tooltip weg
                }
                // Langes Drücken ohne Ziehen: Tooltip der Fähigkeit (wie Hover am PC)
                if (!b.Dragged && !b.Info && Time.unscaledTime - b.StartTime >= MobileUiMath.LongPressSeconds &&
                    !TouchControlMath.IsDragging(off, DragStartPx))
                    ShowButtonInfo(i, b);
                if (b.Kind == TouchAimKind.None) continue;
                if (!b.Dragged) continue;
                b.Cancel = TouchControlMath.IsCancel(off, CancelRadiusPx);
                if (b.Cancel)
                {
                    b.Aiming = false;
                    continue;
                }
                if (TouchControlMath.DragToGround(off, CancelRadiusPx, DragFullPx, CameraFollow.Yaw, b.Range, DragMinFraction, out Vector3 dir, out float dist))
                {
                    b.AimDir = dir;
                    b.AimDist = b.Kind == TouchAimKind.Point ? dist : b.Range;
                    b.Aiming = true;
                }
            }
        }

        private void EndButton(int i, AbilitySlot slot)
        {
            var b = _buttons[i];
            bool info = b.Info;
            if (info) HideButtonInfo(b);
            if (!b.Hold && b.Kind != TouchAimKind.None)
            {
                if (info && !b.Dragged)
                {
                    // nur angesehen (Tooltip) → nichts auslösen
                }
                else if (!b.Dragged)
                {
                    // Antippen: Element-Fähigkeiten aufs nächste Ziel, Mobilität (RMB-Slot) in Blick-/Laufrichtung
                    _slotPressed[(int)slot] = true;
                    if (slot != AbilitySlot.Secondary) SetFrameAim(AutoAim(b.Range, b.Kind == TouchAimKind.Point));
                    else SetFrameAim(TouchAim.Keep);
                }
                else if (!b.Cancel && b.Aiming)
                {
                    _slotPressed[(int)slot] = true;
                    SetFrameAim(DragAim(b));
                }
                // zurück auf den Knopf gezogen → abgebrochen, nichts auslösen
            }
            ReleaseMine(b.Finger);
            ResetButton(b);
        }

        private TouchAim DragAim(ButtonState b)
        {
            if (b.Kind == TouchAimKind.Point && _player != null)
            {
                Vector3 origin = _player.transform.position;
                Vector3 p = origin + b.AimDir * b.AimDist;
                if (_abilities != null) p.y = _abilities.GetGroundHeight(p, origin.y);
                return new TouchAim { Mode = TouchAimMode.Point, Point = p, Direction = b.AimDir, HasPoint = true };
            }
            return new TouchAim { Mode = TouchAimMode.Direction, Direction = b.AimDir, Distance = b.Range, HasPoint = true };
        }

        // Laufendes Zieh-Zielen für die Bodenvorschau (TouchAimPreview)
        public bool TryGetAimPreview(out TouchAimKind kind, out Vector3 direction, out float distance, out float range, out float area)
        {
            for (int i = 0; i < ButtonCount; i++)
            {
                var b = _buttons[i];
                if (b.Finger < 0 || !b.Aiming || b.Cancel) continue;
                kind = b.Kind;
                direction = b.AimDir;
                distance = b.AimDist;
                range = b.Range;
                area = b.Area;
                return true;
            }
            kind = TouchAimKind.None;
            direction = Vector3.forward;
            distance = range = area = 0f;
            return false;
        }

        // ---------------- Sprung ----------------

        private void UpdateJump()
        {
            if (_jumpFinger < 0 || Alive(_jumpFinger)) return;
            ReleaseMine(_jumpFinger);
            _jumpFinger = -1;
        }

        // ---------------- Kamera: Zwei-Finger-Drehen ----------------

        private void AddCameraCandidate(int f, Vector2 p)
        {
            float now = Time.unscaledTime;
            if (_camA < 0)
            {
                _camA = f;
                _camPrevA = _camStartA = p;
                _camTimeA = now;
                _pairValid = false;
            }
            else if (_camB < 0)
            {
                _camB = f;
                _camPrevB = _camStartB = p;
                _camTimeB = now;
                _twistAccum = 0f;
                _camTravel = 0f;
                _pairValid = now - _camTimeA <= TwoFingerStartGap && (PosOf(_camA) - _camStartA).magnitude <= TwoFingerTapTravelDp * _pxPerDp;
                _camPrevA = PosOf(_camA);
            }
            else _ignored[f] = true; // dritter Finger zählt nicht
        }

        // Kamera-Kandidat an ein anderes System verloren (Bauen zählt nicht – es gibt den Finger beim zweiten Welt-Finger frei)
        private bool LostToOther(int f)
        {
            var o = TouchZones.OwnerOf(f);
            return o != TouchZones.Owner.None && o != TouchZones.Owner.Camera && o != TouchZones.Owner.Build;
        }

        private void UpdateCamera()
        {
            float now = Time.unscaledTime;
            bool aGone = _camA >= 0 && (!Alive(_camA) || LostToOther(_camA));
            bool bGone = _camB >= 0 && (!Alive(_camB) || LostToOther(_camB));

            if ((aGone || bGone) && _camA >= 0 && _camB >= 0)
            {
                // Paar endet: Zwei-Finger-Tipp prüfen (beide kurz, kaum bewegt, nicht gedreht)
                if (_pairValid && !_twisting && TouchControlMath.IsTap(now - _camTimeB, _camTravel, TwoFingerTapTime, TwoFingerTapTravelDp * _pxPerDp))
                {
                    if (TouchControlMath.IsDoubleTap(_lastTwoTapTime, now, DoubleTapWindow))
                    {
                        _cameraReset = true;
                        _lastTwoTapTime = float.NegativeInfinity;
                    }
                    else _lastTwoTapTime = now;
                }
                // Der verbleibende Finger dreht nicht weiter; nach einer Drehung bleibt er bis zum Loslassen bei der Kamera
                int rest = aGone ? (bGone ? -1 : _camB) : _camA;
                if (aGone && !LostToOther(_camA)) ReleaseMine(_camA);
                if (bGone && !LostToOther(_camB)) ReleaseMine(_camB);
                if (rest >= 0) _ignored[rest] = true;
                _camA = _camB = -1;
                _twisting = _pairValid = false;
                _twistAccum = 0f;
                return;
            }
            if (aGone)
            {
                if (!LostToOther(_camA)) ReleaseMine(_camA);
                else _ignored[_camA] = true;
                _camA = -1;
            }
            if (_camA < 0 || _camB < 0) return;

            Vector2 a = PosOf(_camA), b = PosOf(_camB);
            _camTravel = Mathf.Max(_camTravel, Mathf.Max((a - _camStartA).magnitude, (b - _camStartB).magnitude));
            float d = TouchControlMath.TwistDelta(_camPrevA, _camPrevB, a, b, TwistMinSpanDp * _pxPerDp);
            if (!_twisting)
            {
                _twistAccum += d;
                if (Mathf.Abs(_twistAccum) >= TwistStartDeg)
                {
                    _twisting = true;
                    TouchZones.Claim(_camA, TouchZones.Owner.Camera);
                    TouchZones.Claim(_camB, TouchZones.Owner.Camera);
                    _yawDelta += TouchControlMath.TwistToYaw(_twistAccum, TwistSensitivity);
                }
            }
            else _yawDelta += TouchControlMath.TwistToYaw(d, TwistSensitivity);
            _camPrevA = a;
            _camPrevB = b;
        }

        // ---------------- Ziel ----------------

        private void SetFrameAim(TouchAim aim)
        {
            _frameAim = aim;
            _frameAimSet = true;
        }

        private void ResolveAim()
        {
            float now = Time.unscaledTime;
            if (_frameAimSet)
            {
                _aim = _frameAim;
                _lastAimTime = now;
                return;
            }
            // Laufendes Zieh-Zielen: Figur dreht sich schon zur Vorschau
            for (int i = 0; i < ButtonCount; i++)
            {
                var b = _buttons[i];
                if (b.Finger < 0 || !b.Aiming || b.Cancel) continue;
                _aim = DragAim(b);
                _lastAimTime = now;
                return;
            }
            if (_atkFiring)
            {
                _aim = _atkAuto
                    ? AutoAim(PrimaryRange(), false)
                    : new TouchAim { Mode = TouchAimMode.Direction, Direction = _atkDir, Distance = PrimaryRange(), HasPoint = false };
                _lastAimTime = now;
                return;
            }
            _aim = now - _lastAimTime < AimHoldTime ? TouchAim.Keep : TouchAim.FaceMove;
        }

        private readonly List<Vector3> _candPos = new List<Vector3>();
        private readonly List<EnemyBrain> _candRef = new List<EnemyBrain>();

        // Nächster Gegner im Suchradius der Fähigkeit; ohne Ziel bleibt die Blickrichtung (Angriff geradeaus).
        // Beim Dauerfeuer wird das Ziel nur alle 0,15 s neu gewählt (oder sobald es fällt).
        private TouchAim AutoAim(float range, bool pointAtFeet)
        {
            if (_player == null) return TouchAim.Keep;
            float now = Time.unscaledTime;
            bool targetGone = _autoTarget == null || _autoTarget.IsDead || !_autoTarget.isActiveAndEnabled;
            if (targetGone || now >= _autoPickAt || !_atkFiring)
            {
                _autoPickAt = now + 0.15f;
                _autoTarget = PickTarget(TouchControlMath.AutoAimRadius(range, AutoAimMin, AutoAimMax, AutoAimFallback));
            }
            if (_autoTarget == null) return TouchAim.Keep;
            Vector3 p = _autoTarget.transform.position;
            if (pointAtFeet && _abilities != null) p.y = _abilities.GetGroundHeight(p, p.y);
            Vector3 to = p - _player.transform.position;
            to.y = 0f;
            return new TouchAim
            {
                Mode = TouchAimMode.Point,
                Point = p,
                Direction = to.sqrMagnitude > 1e-6f ? to.normalized : _player.transform.forward,
                HasPoint = true
            };
        }

        private EnemyBrain PickTarget(float radius)
        {
            _candPos.Clear();
            _candRef.Clear();
            var all = EnemyBrain.All;
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                _candPos.Add(e.transform.position);
                _candRef.Add(e);
            }
            Transform t = _player.transform;
            int best = TouchControlMath.PickAutoTarget(t.position, t.forward, _candPos, radius, FacingWeight);
            var result = best >= 0 ? _candRef[best] : null;
            _candRef.Clear();
            return result;
        }
    }
}
