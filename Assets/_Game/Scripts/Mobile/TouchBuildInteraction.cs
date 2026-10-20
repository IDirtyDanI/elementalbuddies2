using System.Collections.Generic;
using UnityEngine;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ElementalBuddies
{
    // Touch-Teil des InteractionManagers (Mobil-Version, Paket B „Bauen“). Läuft nur bei MobilePlatform.TouchMode; der
    // PC-Pfad (Maus/Tasten) in InteractionManager.cs bleibt unverändert.
    //
    // Ein Finger, der in der Welt-Zone (TouchZones) auf keinem UI-Element aufsetzt, gehört dem Bauen (Owner.Build):
    // - Bau-Geist aktiv: Ziehen → Geist folgt (TouchBuildRules.GhostLiftDp über dem Finger), Tippen → Geist an die Stelle;
    //   ✓/✗ am Geist (TouchBuildOverlay) bauen bzw. brechen ab. Langes Drücken tut dann nichts.
    // - Kein Geist: Tippen → Buddy auswählen (vergrößerte Trefferzone) bzw. ins Leere → abwählen; langes Drücken → Ping.
    // Ein zweiter Finger in der Welt = Kamera-Geste (Paket S): eigene Geste abbrechen, Finger freigeben und warten, bis
    // alle Welt-Finger weg sind. Bau-Leisten-Slot antippen (BuildBarUI) → Geist erscheint an einem gültigen Platz vor der Figur.
    public partial class InteractionManager
    {
        private int _touchFinger = -1;          // Finger-Index der eigenen Geste, −1 = keine
        private TouchGesture _touchGesture;
        private bool _touchSuppressed;          // nach Zwei-Finger-Geste: bis alle Welt-Finger weg sind
        private readonly HashSet<int> _foreignFingers = new HashSet<int>(); // auf UI/Steuerung begonnen → ignorieren
        private readonly List<int> _seenFingers = new List<int>();
        private readonly List<int> _pruneFingers = new List<int>();
        private TouchBuildOverlay _touchOverlay;
        private Vector3 _touchGhostPos;
        private readonly List<Vector2Int> _searchCells = new List<Vector2Int>();
        private readonly List<Vector3> _pickPositions = new List<Vector3>();
        private readonly List<ElementalBuddy> _pickBuddies = new List<ElementalBuddy>();
        private readonly Collider[] _pickBuffer = new Collider[32];

        private bool TouchGhostActive => _selectedUnitConfig != null && _currentGhost != null;

        // ---------------- Finger-Verwaltung ----------------

        private void HandleTouchInput()
        {
            TouchZones.EnsureEnabled();
            EnsureEditorTouchSimulation();

            var touches = ETouch.activeTouches;
            float now = Time.unscaledTime;
            float slop = MobilePlatform.DpToPixels(TouchGesture.TapSlopDp);

            bool mineSeen = false, mineEnded = false;
            Vector2 minePos = _touchGesture.LastPos;
            int otherWorldFingers = 0;
            _seenFingers.Clear();

            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                int id = t.finger.index;
                var phase = t.phase;
                bool ended = phase == ETouchPhase.Ended || phase == ETouchPhase.Canceled;
                _seenFingers.Add(id);

                if (id == _touchFinger)
                {
                    mineSeen = true;
                    mineEnded = ended;
                    minePos = t.screenPosition;
                    continue;
                }

                if (phase == ETouchPhase.Began)
                {
                    _foreignFingers.Remove(id); // Finger-Index wird nach dem Loslassen neu vergeben
                    Vector2 p = t.screenPosition;
                    bool world = TouchZones.InWorldZone(p) && !TouchZones.IsClaimed(id) && !TouchBuildUi.IsOverUI(p);
                    if (!world)
                    {
                        _foreignFingers.Add(id);
                        continue;
                    }
                    if (_touchFinger < 0 && !_touchSuppressed && otherWorldFingers == 0)
                    {
                        _touchFinger = id;
                        TouchZones.Claim(id, TouchZones.Owner.Build);
                        _touchGesture = TouchGesture.Begin(p, now);
                        mineSeen = true;
                        mineEnded = false;
                        minePos = p;
                        continue;
                    }
                }

                if (ended)
                {
                    _foreignFingers.Remove(id);
                    continue;
                }
                if (_foreignFingers.Contains(id)) continue;

                // Liegender Finger in der Welt, der weder Sticks noch Fähigkeits-Knöpfen noch der UI gehört → Kamera-Geste
                var owner = TouchZones.OwnerOf(id);
                if (owner == TouchZones.Owner.None || owner == TouchZones.Owner.Camera || owner == TouchZones.Owner.Build)
                    otherWorldFingers++;
            }

            // Fremd-Finger, die ohne „Ended“ verschwunden sind (z. B. während Pause), vergessen
            if (_foreignFingers.Count > 0)
            {
                _pruneFingers.Clear();
                foreach (int id in _foreignFingers)
                    if (!_seenFingers.Contains(id)) _pruneFingers.Add(id);
                for (int i = 0; i < _pruneFingers.Count; i++) _foreignFingers.Remove(_pruneFingers[i]);
            }

            if (_touchFinger >= 0)
            {
                if (otherWorldFingers > 0)
                {
                    // Zweiter Finger in der Welt → Kamera-Geste; Geist bleibt, wo er ist
                    CancelTouchGesture();
                    _touchSuppressed = true;
                }
                else if (!mineSeen || mineEnded)
                {
                    bool wasDrag = _touchGesture.State == TouchGestureState.Drag;
                    var e = _touchGesture.End(minePos, now, slop, TouchGesture.LongPressSeconds);
                    ReleaseTouchFinger();
                    DispatchTouchEvent(e, minePos);
                    if (wasDrag || e == TouchGestureEvent.DragStarted) MoveTouchGhostToFinger(minePos);
                }
                else
                {
                    var e = _touchGesture.Update(minePos, now, slop, TouchGesture.LongPressSeconds);
                    DispatchTouchEvent(e, minePos);
                    if (_touchGesture.State == TouchGestureState.Drag) MoveTouchGhostToFinger(minePos);
                }
            }
            else if (_touchSuppressed && otherWorldFingers == 0)
            {
                _touchSuppressed = false;
            }
        }

        private void DispatchTouchEvent(TouchGestureEvent e, Vector2 screenPos)
        {
            switch (e)
            {
                case TouchGestureEvent.Tap:
                    if (TouchGhostActive)
                    {
                        // Tippen setzt den Geist genau unter den Finger (ohne Versatz)
                        if (ScreenToFloorCell(screenPos, out Vector3 cell)) _touchGhostPos = cell;
                    }
                    else TouchSelectAt(screenPos);
                    break;
                case TouchGestureEvent.LongPress:
                    // Ping nur ohne Bau-Geist (Ziehen des Geists hat Vorrang)
                    if (!TouchGhostActive && PingSystem.Instance != null) PingSystem.Instance.TryPingAtScreen(screenPos);
                    break;
            }
        }

        private void MoveTouchGhostToFinger(Vector2 finger)
        {
            if (!TouchGhostActive) return;
            Vector2 p = TouchBuildRules.GhostScreenPoint(finger, MobilePlatform.DpToPixels(TouchBuildRules.GhostLiftDp),
                new Rect(0f, 0f, Screen.width, Screen.height));
            if (ScreenToFloorCell(p, out Vector3 cell)) _touchGhostPos = cell;
        }

        private void ReleaseTouchFinger()
        {
            if (_touchFinger >= 0 && TouchZones.OwnerOf(_touchFinger) == TouchZones.Owner.Build) TouchZones.Release(_touchFinger);
            _touchFinger = -1;
        }

        // Geste abbrechen (Pause, Kartenfenster, zweiter Finger, Zerstörung); Finger wird freigegeben
        private void CancelTouchGesture()
        {
            _touchGesture.Cancel();
            ReleaseTouchFinger();
        }

        // Editor ohne Device Simulator („Touch-Modus erzwingen“): Maus erzeugt Touches, sonst gäbe es keine Finger
        private static void EnsureEditorTouchSimulation()
        {
#if UNITY_EDITOR
            if (!Application.isMobilePlatform && UnityEngine.InputSystem.Touchscreen.current == null)
                UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
#endif
        }

        // ---------------- Auswahl per Tippen ----------------

        private void TouchSelectAt(Vector2 screenPos)
        {
            if (IsGameOver || LocalInputBlocked) return;
            var buddy = PickBuddyAt(screenPos);
            if (buddy != null) SelectBuddy(buddy);
            else DeselectBuddy();
        }

        // Vergrößerte Trefferzone: 1. genauer Raycast (wie PC), 2. Sphere-Cast entlang des Strahls, 3. nächster Buddy im
        // Radius TouchBuildRules.PickRadius um den Bodenpunkt
        private ElementalBuddy PickBuddyAt(Vector2 screenPos)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return null;

            int mask = BuddyLayer.value;
            if (mask == 0) mask = LayerMask.GetMask("Buddy");
            if (mask == 0) mask = Physics.AllLayers;

            Ray ray = _mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, mask, QueryTriggerInteraction.Collide))
            {
                var b = hit.collider.GetComponentInParent<ElementalBuddy>();
                if (b != null && !b.IsDead) return b;
            }
            if (Physics.SphereCast(ray, TouchBuildRules.PickCastRadius, out hit, 100f, mask, QueryTriggerInteraction.Collide))
            {
                var b = hit.collider.GetComponentInParent<ElementalBuddy>();
                if (b != null && !b.IsDead) return b;
            }

            if (!ScreenToFloor(screenPos, out Vector3 ground)) return null;
            int n = Physics.OverlapCapsuleNonAlloc(ground, ground + Vector3.up * 2.5f, TouchBuildRules.PickRadius, _pickBuffer, mask,
                QueryTriggerInteraction.Collide);
            _pickBuddies.Clear();
            _pickPositions.Clear();
            for (int i = 0; i < n; i++)
            {
                var b = _pickBuffer[i] != null ? _pickBuffer[i].GetComponentInParent<ElementalBuddy>() : null;
                if (b == null || b.IsDead || _pickBuddies.Contains(b)) continue;
                _pickBuddies.Add(b);
                _pickPositions.Add(b.transform.position);
            }
            // Collider-Rand im Radius, Mittelpunkt etwas weiter: großzügig wählen (Radius + halbe Zelle)
            int best = TouchBuildRules.PickNearest(ground, _pickPositions, TouchBuildRules.PickRadius + 0.5f);
            return best >= 0 ? _pickBuddies[best] : null;
        }

        // ---------------- Bau-Geist ----------------

        // Bodenpunkt unter einem Bildschirmpunkt (gleicher Boden-Raycast wie am PC)
        private bool ScreenToFloor(Vector2 screenPos, out Vector3 point)
        {
            point = Vector3.zero;
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera == null) return false;
            Ray ray = _mainCamera.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out RaycastHit hit, 100f, FloorLayer)) return false;
            point = hit.point;
            return true;
        }

        private bool ScreenToFloorCell(Vector2 screenPos, out Vector3 cell)
        {
            bool ok = ScreenToFloor(screenPos, out cell);
            if (ok) cell = TouchBuildRules.SnapToGrid(cell);
            return ok;
        }

        // Bodenhöhe an einer Rasterstelle (wie ServerBuild); ohne FloorLayer bleibt die Höhe
        private bool FloorAt(Vector3 p, out Vector3 floor)
        {
            floor = p;
            if (FloorLayer.value == 0) return true;
            if (!Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 20f, FloorLayer)) return false;
            floor.y = hit.point.y;
            return true;
        }

        // Nach dem Antippen eines Bau-Leisten-Slots: Geist an den ersten gültigen Rasterplatz nahe der Figur (in
        // Kamera-Blickrichtung vor ihr), sonst am Bildschirm-Mittelpunkt. Kein gültiger Platz → Startpunkt (rot).
        // keepPosition: anderer Buddy-Typ gewählt, während schon ein Geist stand → Platz behalten.
        private void PlaceTouchGhostInitial(bool keepPosition)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (keepPosition && IsTouchReachableOnScreen(_touchGhostPos))
            {
                if (_currentGhost != null) _currentGhost.transform.position = _touchGhostPos;
                return;
            }
            Vector3 origin = Vector3.zero;
            bool haveOrigin = false;

            Transform figure = PlayerAvatar.Local != null ? PlayerAvatar.Local.transform : null;
            if (figure != null)
            {
                Vector3 fwd = _mainCamera != null ? _mainCamera.transform.forward : Vector3.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                haveOrigin = FloorAt(figure.position + fwd.normalized * TouchBuildRules.StartDistance, out origin);
            }
            if (!haveOrigin)
                haveOrigin = ScreenToFloor(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out origin);
            if (!haveOrigin && figure != null) origin = figure.position;

            Vector3 start = TouchBuildRules.SnapToGrid(origin);
            _touchGhostPos = start;
            TouchBuildRules.SearchOrder(TouchBuildRules.StartSearchRadius, _searchCells);
            for (int i = 0; i < _searchCells.Count; i++)
            {
                Vector3 p = start + new Vector3(_searchCells[i].x, 0f, _searchCells[i].y);
                if (!FloorAt(p, out p)) continue;
                if (!IsTouchReachableOnScreen(p)) continue;
                if (ValidatePlacement(p))
                {
                    _touchGhostPos = p;
                    break;
                }
            }
            if (_currentGhost != null) _currentGhost.transform.position = _touchGhostPos;
        }

        // Platz sichtbar und in der Welt-Zone (nicht unter den Sticks / im unsichtbaren Bereich)?
        private bool IsTouchReachableOnScreen(Vector3 p)
        {
            if (_mainCamera == null) return true;
            Vector3 sp = _mainCamera.WorldToScreenPoint(p);
            if (sp.z <= 0f) return false;
            Rect safe = MobilePlatform.SafeArea;
            if (!safe.Contains(new Vector2(sp.x, sp.y))) return false;
            return TouchZones.InWorldZone(sp);
        }

        // Pro Frame (statt Maus-Verfolgung): Geist an _touchGhostPos, Validierung/Farben wie PC, ✓/✗ nachführen
        private void UpdateTouchGhost()
        {
            if (LocalInputBlocked)
            {
                HideTouchOverlay();
                return;
            }
            _currentGhost.transform.position = _touchGhostPos;
            bool isValid = ValidatePlacement(_touchGhostPos);
            UpdateGhostVisuals(isValid);

            EnsureTouchOverlay();
            Vector3 sp = _mainCamera.WorldToScreenPoint(_touchGhostPos);
            if (sp.z <= 0f) _touchOverlay.Hide();
            else _touchOverlay.Show(sp, isValid);
        }

        private void EnsureTouchOverlay()
        {
            if (_touchOverlay != null) return;
            _touchOverlay = TouchBuildOverlay.Create(transform);
            _touchOverlay.OnConfirm += TouchConfirmBuild;
            _touchOverlay.OnCancel += TouchCancelBuild;
        }

        private void HideTouchOverlay()
        {
            if (_touchOverlay != null) _touchOverlay.Hide();
        }

        // ✓: gleiche Prüfung wie am PC, ungültig → Grund als Hinweis; gültig → Bau-Anfrage an den Server, Platzieren beenden
        private void TouchConfirmBuild()
        {
            if (!TouchGhostActive || LocalInputBlocked || PauseManager.IsPaused) return;
            Vector3 pos = _touchGhostPos;
            string reason = ValidatePlacement(_selectedUnitConfig, pos, true);
            if (reason != null)
            {
                ToastUI.Show(reason);
                return;
            }
            NetGame.RequestBuild(_selectedIndex, pos);
            Deselect();
        }

        // ✗: Platzieren abbrechen (wie Rechtsklick/ESC am PC)
        private void TouchCancelBuild()
        {
            if (_selectedUnitConfig == null) return;
            Deselect();
        }
    }
}
