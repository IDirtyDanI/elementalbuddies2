\# Projekt: Elemental Action-Tower-Defense (Prototype v0.1)



\## Kontext

Erstellung eines Unity-Prototypen (3D, Top-Down), der "Tower Defense" (Bauen von Elementaren) mit "Action RPG" (Spieler läuft und kämpft selbst) mischt.

Ziel ist ein spielbarer Vertical Slice mit 10 Wellen.



\## Tech Stack \& Architektur

\* \*\*Engine:\*\* Unity 2022.3 LTS+

\* \*\*Input:\*\* New Input System (WASD Move, Mouse Aim, Q/E Skills, 1-4 Build).

\* \*\*Architecture:\*\*

&nbsp;   \* \*\*Data-Driven:\*\* Alle Balance-Werte (Gegner, Türme, Wellen) in `ScriptableObjects`.

&nbsp;   \* \*\*Singleton Managers:\*\* `GameManager` (State), `EconomyManager` (Mana), `WaveManager` (Spawns), `InteractionManager` (Build/Cast).

&nbsp;   \* \*\*Event-Bus:\*\* C# Actions für Decoupling (z.B. `OnWaveStart`, `OnEnemyKilled`).

&nbsp;   \* \*\*AI:\*\* NavMeshAgent für Gegner.



---



\## Daten-Strukturen \& Configs (JSON/ScriptableObject Vorlage)



\### 1. Stats \& Balancing (Defaults)

Die folgenden Werte sollen als Standard in den ScriptableObjects hinterlegt werden.



\*\*Economy Settings:\*\*

\* StartMana: 100

\* ManaCap: 200

\* RegenOutCombat: 1.5/s

\* RegenInCombat: 0.5/s

\* CombatSurcharge: 25% (Multiplikator 1.25 für Baukosten während der Welle)

\* RefundRatio: 0.7



\*\*Unit Stats (Towers):\*\*

| Typ | Kosten (Out/In) | Range | Rate (s) | Dmg | Besonderheit |

| :--- | :--- | :--- | :--- | :--- | :--- |

| \*\*Feuer\*\* | 60 / 75 | 7m | 1.4 | 12 | Prio: Closest. Hohe DPS. |

| \*\*Eis\*\* | 55 / 69 | 6m | 0.8 | 6 | Slow: 25% (2.5s), Cap 50%. |

| \*\*Erde\*\* | 70 / 88 | - | - | 8 (DPS) | \*\*Taunt\*\* (4m Radius, 2s Dauer, 6s CD). HP-Budget: 100. DmgReduct: 15%. |

| \*\*Licht\*\* | 65 / 81 | 5m | 2.5 | - | \*\*Heal\*\*: 8 HP auf Player. |



\*\*Player Stats:\*\*

\* HP: 100

\* Speed: 6.0 m/s

\* \*\*Skill Q (Arcane Ball):\*\* 15 Mana, 1s CD, 35 Dmg.

\* \*\*Skill E (Blink):\*\* 30 Mana, 8s CD, 8m Range, 0.4s Invulnerability.



\*\*Enemy (Runner):\*\*

\* Base HP: 60 (+8 pro Welle)

\* Speed: 3.2 m/s

\* Dmg: 10 DPS (am Spieler)

\* Logic: Pathfinding zum Spieler. Wenn blockiert > 0.6s -> Angriff auf Blocker.



---



\## Implementierungs-Schritte (Tasks)



\### Phase 1: Core Setup \& Player

1\.  \*\*Project Init:\*\* Setup URP (optional) oder Built-in. Setup NavMesh Surface. Setup Input Actions (Move, Aim, Fire, BuildHotkeys).

2\.  \*\*Player Controller:\*\*

&nbsp;   \* Movement (CharacterController + New Input System).

&nbsp;   \* Aiming (Raycast from Camera to Floor Plane).

&nbsp;   \* Ability System: `CastSpell(Type)` Logik.

&nbsp;   \* Implementiere `Arcane Ball` (Projectile) und `Blink` (Transform change mit Coroutine für I-Frames).



\### Phase 2: Economy \& Building System

1\.  \*\*EconomyManager:\*\* Verwalten von CurrentMana, MaxMana. Logik für `TrySpendMana(amount)` und Regeneration (abhängig vom GameState `InWave` vs `Rest`).

2\.  \*\*Build System:\*\*

&nbsp;   \* Ghost-Anzeige beim Drücken von 1-4.

&nbsp;   \* Grid-Snapping oder freie Platzierung (Radius Check gegen Überlappung).

&nbsp;   \* Kostenberechnung (Check `GameManager.IsCombat` für Surcharge).

&nbsp;   \* Instanziierung der Elementar-Prefabs.



\### Phase 3: Tower Logik (Elementare)

1\.  \*\*Base Class `ElementalBuddy`:\*\*

&nbsp;   \* Erbt von MonoBehaviour.

&nbsp;   \* Hat `BuddyConfig` (ScriptableObject).

&nbsp;   \* State Machine: `Idle`, `Attack` (oder `Heal/Taunt`).

2\.  \*\*Spezifische Logik:\*\*

&nbsp;   \* \*\*Feuer/Eis:\*\* Projectile Shooting mit Vorhaltewinkel (Prediction) oder Homing. Eis appliziert `SlowEffect` Komponente auf Gegner.

&nbsp;   \* \*\*Licht:\*\* Checkt Distanz zum Player -> `Player.Heal()`.

&nbsp;   \* \*\*Erde (Komplex):\*\* Hat `HealthBudget`. Zieht Aggro (Taunt Pulse). Wenn Budget <= 0 -> `Destroy()`.



\### Phase 4: Enemy AI \& Wave Spawning

1\.  \*\*Enemy AI (`EnemyBrain`):\*\*

&nbsp;   \* NavMeshAgent Setup.

&nbsp;   \* \*\*Targeting Priority:\*\*

&nbsp;       1.  Active Taunt (Erde) in Range? -> Target Taunt.

&nbsp;       2.  PathComplete zum Player möglich? -> Target Player.

&nbsp;       3.  \*\*Pathfail Logic (Anti-Cheese):\*\*

&nbsp;           ```csharp

&nbsp;           // Pseudocode für Pathfail

&nbsp;           if (agent.velocity.magnitude < 0.1f \&\& !ReachedDestination) {

&nbsp;               stuckTimer += Time.deltaTime;

&nbsp;           } else { stuckTimer = 0; }



&nbsp;           if (stuckTimer > 0.6f) {

&nbsp;               // Finde nächsten "Blocker" (Layer Mask 'Buddy')

&nbsp;               Collider\[] hits = Physics.OverlapSphere(transform.position, 2.0f, buddyLayer);

&nbsp;               var target = GetClosest(hits);

&nbsp;               Attack(target); // Zerstöre den Wegbereiter

&nbsp;           }

&nbsp;           ```

2\.  \*\*WaveManager:\*\*

&nbsp;   \* Liste von Wave-Configs (Count, Interval).

&nbsp;   \* Spawning Logik an festen Spawn-Punkten.

&nbsp;   \* Event `OnWaveEnd`: Gib Mana-Bonus.



\### Phase 5: UI \& Game Loop

1\.  \*\*HUD:\*\*

&nbsp;   \* Mana Bar \& HP Bar.

&nbsp;   \* Skill Icons (Cooldown Overlay).

&nbsp;   \* Buddy Leiste (Kosten anzeigen, rot wenn zu teuer).

&nbsp;   \* Wave Counter.

2\.  \*\*Game Loop:\*\*

&nbsp;   \* Start -> Build Phase -> Start Wave Button -> Combat -> End Wave -> Loop.

&nbsp;   \* Win/Lose Conditions.



---



\## Task-Master Workflow (Referenz)



Nutze diese Befehle im Terminal (im Projektordner), um die Entwicklung zu steuern:



1\.  \*\*Setup \& Config:\*\*

&nbsp;   \* Modelle konfigurieren: `task-master models`

&nbsp;   \* API Keys setzen: In `.env` oder `.cursor/mcp.json`

2\.  \*\*Tasks generieren (Start):\*\*

&nbsp;   \* \*Hinweis: Diese `idee.md` fungiert als dein PRD.\*

&nbsp;   \* CLI: `task-master parse-prd idee.md` (Pfade ggf. anpassen)

3\.  \*\*Analyse:\*\*

&nbsp;   \* CLI: `task-master analyze-complexity --research`

4\.  \*\*Tasks erweitern:\*\*

&nbsp;   \* CLI: `task-master expand --all --research`

5\.  \*\*Entwicklung starten:\*\*

&nbsp;   \* CLI: `task-master next`

6\.  \*\*Extras:\*\*

&nbsp;   \* Alle Befehle: `task-master --help`

&nbsp;   \* IDE Regeln: `tm rules --setup`

