# Wheelie player animations

`Wheelie_PlayerAnimations.blend` contains the existing Wheelie mesh/rig and 66
editable actions authored and baked with Blender 5.2.3. The original Wheelie AI
model and the regular player's Blender source are preserved.

The animation library includes all 56 actions in the regular player's Blender
source: idle variations, directional movement, sprint, jumping/landing, carrying,
pickup/drop, held items, pointing, reactions, death, celebration, exhaustion,
punches, and the pistol, assault rifle, and shotgun animation sets. Wheelie also
has bat ready/swing, magnetic carry transitions, and all six player emote poses.
Mechanical arm poses are adapted to Wheelie's shorter arms and grip sockets;
movement includes wheel rolling and suspension movement.

Saved Unity assets:

- `Assets/_Project/Animations/Wheelie/Clips/`: 66 editable native animation clips.
- `Assets/_Project/Animations/Wheelie/WheeliePlayer.controller`: player state
  machine, weapon movement/aim trees, punches, bat, reloads, and additional poses.
- `Assets/_Project/Animations/Wheelie/WheelieMenu.controller`: idle and entrance
  movement for the existing main-menu transition.
- `Assets/_Project/Prefabs/Players/WheeliePlayer.prefab`: Animator, explicit rig
  references, and per-character emote clip overrides. The earlier procedural
  whole-body presentation component is disabled to avoid fighting the Animator.
- `Assets/_Project/Prefabs/UI/WheelieMenuVisual.prefab`: independent saved menu
  preview, now animated. It retains its menu-specific scale and placement.

The player model's local Y is 0.67. With the existing CharacterController's
centre/height/skin width, the actor settles at root Y -0.655 on a flat floor,
leaving approximately 0.015 m of visual ground clearance. The actor origin is
therefore intentionally different from the wheel's ground-contact point.

`Tools/Wheelie/author_player_animations.py` is an explicit offline Blender tool.
It writes the separate player animation source, animation-only FBX, and action
manifest. It never runs on import or Play mode. Unity clips/controllers and
prefabs are saved assets and can be edited directly without rerunning it.

Headless validation checked all clip bindings, sampled arm movement, the regular
player's locomotion, CharacterController ground clearance, and forward barrel
alignment for pistol, assault rifle, and shotgun aim poses. A complete live
multiplayer gameplay session was not run as part of this animation check.
