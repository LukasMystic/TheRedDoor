# The Red Door

A short, deliberately unforgiving boss demo about resilience. You wake in a hollow with moss grown over your hands, walk east, and meet the thing that has been keeping the way.

Built in Unity 6 (6000.4.1f1) with the Universal Render Pipeline, 2D.

<!-- Drop a screenshot or a short GIF here — the arena with The Keeper mid-slam reads best. -->
<!-- ![The Keeper](docs/keeper.png) -->

---

## The demo in one run

1. **The hollow** — a short traversal section. Four lines of story sit in the world itself and fade as you pass them. There is no cutscene and no text box; the lines only continue eastward, so the direction is taught by layout rather than by an instruction.
2. **The arena** — a gate seals behind you and The Keeper wakes.
3. **The Keeper** — a three-phase fight. Swipe, charge, ground slam with twin shockwaves, and a committed heavy strike.
4. **The red door** — it unlocks when the Keeper falls. Opening it ends the demo.

**Controls** — `A` / `D` move · `Space` jump · `Left Shift` dash · `J` attack · `E` interact · `Esc` pause

---

## Design notes

The parts worth talking about are the decisions, not the feature list.

**Difficulty is tuned on recovery, not on telegraphs.** Recovery is the player's free time, so shortening it by 60% is what makes the fight demanding. Shortening the *warning* would just make it unfair. Telegraphs come down only 24%, and phase one is left close to as authored so the first read of the fight is still teachable — a fight nobody survives long enough to learn is not difficult, it is opaque.

**Feedback separates events from states.** Being hit flashes a radial vignette and a brief wash. Critical health lights the left and right screen edges instead. A momentary event and an ongoing condition should never share a visual language, or the player stops reading either.

**The cooldown gauge rides on the player.** It first sat in the top-left corner beside the health, which meant checking your swing timing required looking away from the boss mid-fight. It now tracks the player's screen position, and only exists during the encounter.

**The Keeper's reach is measured, not authored.** Its arena limits were hand-set numbers that fell behind the level as the floor grew, leaving strips at each end where the boss could not follow and no shockwave could reach — a safe corner created by drift rather than by player cleverness. The limits are now derived at runtime by raycasting the floor the boss stands on, so reach and level cannot fall out of step again.

**The dash passes through the boss**, and the collision ignore is held past the end of the dash until the shapes separate — restoring it while the player is inside the boss would have the physics engine eject them.

---

## Project layout

| Area | Scripts |
| --- | --- |
| **Boss** | `KeeperController` (phased state machine, pursuit, arena fitting), `BossHealth`, `GroundShockwave`, `KeeperAnimationController` |
| **Player** | `PlayerController` (movement, dash, i-frames), `PlayerCombat`, `PlayerHealth`, `PlayerAnimationController` |
| **World** | `WorldStory`, `RespawnManager`, `ArenaGate`, `ArenaCheckpoint`, `RedDoor`, `FallHazard`, `CameraFollow2D` (follow + impact shake) |
| **UI** | `GameFlowUI` (title, pause, end, credits), `PlayerFeedbackUI`, `PlayerHealthUI`, `BossHealthUI`, `RedDoorUI`, `TutorialControlsUI` |
| **Audio** | `DemoAudio` (music state machine, ambience bed, all one-shots) |

The menus, the damage feedback and the world story are constructed in code at runtime rather than authored as prefabs, so they carry no scene wiring.

---

## Credits

**Art**

- Mossy Cavern — Maaot
- 2D Hand-Drawn Player — ForsakenVoid
- Moss Guardian — colingx
- Leaf & Branch UI — Coarsecurve / A. Moseley
- Forest Green UI Pack — Gamified soul
- Hand Painted Platformer, Dungeon — oleekconder

**Audio**

- Sound effects — Kenney (CC0)
- Forest ambience — "Forest Ambience" by TinyWorlds (OpenGameArt, CC0)
- Exploration & ending themes — adapted from "Peaceful Forest" by Samza (OpenGameArt, CC0)
- Boss theme, title theme, the Keeper's voice and the UI sounds were synthesised for this project

Developed by **Stanley Pratama Teguh**. Made with Unity.

---

## Running it

Open the project in Unity 6000.4.1f1 and load `Assets/Scenes/Game.unity`. There is no build-time setup — the demo boots to its own title screen.
