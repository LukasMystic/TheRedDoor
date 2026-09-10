# TheRedDoor audio credits

Downloaded 2026-09-09. These sources explicitly offer the selected audio under
Creative Commons Zero (CC0 1.0): https://creativecommons.org/publicdomain/zero/1.0/
No purchase, subscription or attribution condition is required; creators are credited here voluntarily.

- **Kenney — RPG Audio**: https://kenney.nl/assets/rpg-audio
  Original license included in `SFX/KenneyRPG/License.txt`.
  Cloth movement/jump/dash, knife swings, wooden door/gate movement and creak warning.
- **Kenney — Impact Sounds**: https://kenney.nl/assets/impact-sounds
  Original license included in `SFX/KenneyImpact/License.txt`.
  Grass footsteps, punch/wood damage, mining/slam impact and victory bell.
- **TinyWorlds — Forest Ambience**: https://opengameart.org/content/forest-ambience
  Original `Forest_Ambience.mp3`, seamless environmental loop.
- **Samza — Peaceful Forest**: https://opengameart.org/content/peaceful-forest
  Original `Peaceful Forest.wav`, saved as `Peaceful_Forest.wav`; exploration/after-boss music.
- **RandomMind — Medieval: Battle**: https://opengameart.org/content/medieval-battle
  Original `battle_8.mp3`, saved as `Medieval_Battle.mp3`; boss music.

Source recordings are unchanged. Unity compresses music for streaming playback and
decodes short mono sound effects on load. Selected effects are pitched/mixed at runtime.
Exploration and battle tracks repeat with AudioSource looping; only Forest Ambience
is explicitly advertised by its creator as a seamless loop.

## Processed demo and menu audio

The original downloads above remain in `Assets/Audio`. The game now prefers the
processed OGG set in `Assets/Resources/Audio`; these files include level matching,
loop preparation and layered effects. The outro is derived from the exploration
recording. The current Keeper voice and `Music_Boss` are synthesized replacements,
not the original Medieval: Battle recording.

The opening `Music_Title` and `UI_Hover`, `UI_Click`, `UI_Back` were synthesized for
this project (a sparse minor-key theme and short wooden UI ticks). They do not use
an additional downloaded recording. `GameFlowUI` loads these automatically;
the opening theme replaces gameplay music on the title page, and pause ducks the
current gameplay beds. Menu effects continue at zero time scale.

## Mix controls

Select the scene `GameAudio` object, then **Demo Audio → Mix**. Master, Music,
Ambience and SFX can be tuned independently (0 mutes). No audio singleton survives
scene reload, so respawns cannot stack music. Music fades between exploration and
the arena; damage, player/boss attacks, footsteps, jumps, dash, gates and ending use
separate non-spatial SFX channels so camera position does not change loudness.
