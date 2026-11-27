# Hover Delivery

Hover Delivery is the third choice in **Customise**. The playable prefab and menu preview are editable Unity prefab variants. Both use the existing Couch Guy skeleton. The player variant inherits the original Animator Controller, gameplay components, weapon sockets, and hover effects. Eleven new rigid mesh sections follow the original bone transforms. The wider stance accommodates the existing shoulder and hand placements.

- [Editable Blender source](HoverDelivery.blend)
- [Rigged FBX](../../Assets/_Project/Models/HoverDelivery/HoverDelivery.fbx)
- [Player prefab](../../Assets/_Project/Prefabs/Players/HoverDeliveryPlayer.prefab) and [menu preview prefab](../../Assets/_Project/Prefabs/UI/HoverDeliveryMenuVisual.prefab)
- [Eight-view turnaround](Turnaround.png), [pose and weapon review](PoseReview.png), and [Unity CRT preview](UnityPreview.png)
- [Animation review order](ActionReview/ActionOrder.txt) and [Unity animation inventory](UnityAnimationInventory.txt)

The six Unity materials are in the FBX folder. The visor uses the existing CRT material and expression atlas with its own full-rectangle UV settings. The blank visor in the Blender renders is the model's glass surface. Unity drives the expression at runtime.

## Blender refinement

Each pass used front, rear, left, right, front three-quarter, rear three-quarter, top, and underside renders, compared with the supplied concept and the previous pass.

| Pass | Finding and correction | Visual result |
| --- | --- | --- |
| 1 · Silhouette | The initial helmet and hover dish were narrow. The antennas were too upright and the forearms looked short. Widened the helmet and dish, opened the antenna angle, and filled the cuffs. | The head and base read more clearly as the two main volumes. The wider shoulder envelope follows the original rig. |
| 2 · Surface detail | The visor protruded, the narrow antennas looked like rods, and the rear and underside needed more definition. Recessed the visor, widened the antenna fins, added backpack isolators, and revised the ear and nozzle finishes. | The visor sits closer to the shell and the rear module and concentric plasma shapes read from oblique angles. |
| 3 · Joint clearance | Rounded forearm ends and long fingertips crowded the elbow, wrist, and equipment grip. Tapered the cuffs and shortened the curled fingers. Sampled aim, reload, carry, pickup, punch, downed, and celebration poses. | The mechanical joint breaks remain visible. The compact mitts retain the original weapon and hand anchors. |
| 4 · Armour and equipment | Shoulder plates looked undersized from the front. Enlarged the porcelain cap and moved it forward over the bearing. Rechecked the silhouette and rendered with the inherited rifle and shotgun models. | The shoulder reads as armour from the front. The rifle cleared the helmet in the sampled raise. Shotgun reload can still intersect the lower helmet, which also occurs on the original robot. |

The pose sheet reads left to right across each row. The first row shows Idle, Assault Rifle Raise, Carry Idle, and Pickup. The second shows Punch Right, Death, Celebrate, and Shotgun Reload. The six animation sheets show two samples per action in the sequence recorded in the review order.

## Validation

- Unity 6000.3.11f1 imported the FBX as a Generic rig with 68 takes and 32,916 model triangles. The FBX includes the original Blender actions and 12 sampled Unity actions for the bat, magnetic carry, and emotes. The playable prefab uses the original project clips.
- Headless Unity resolved every transform binding in the 46 distinct Animator Controller clips and sampled each clip at nine times. The variant and default had matching local positions and rotations at each sample. The held item anchors and renderers, face settings, hover pivot, catalog, and network registry entries were checked in the saved assets.
- Headless Play Mode exercised the customisation preview's CRT, host character selection, an independent loopback client's default selection and receipt of Hover Delivery, movement ownership, the idle-to-point blend, all six runtime emotes, replicated weapon equip, downed and revive expressions, couch carry and release, and hoverboard mount and dismount. The isolated hoverboard test staged the board within interaction range.
- The default player prefab, menu preview, source model, rig, and Animator Controller remained unchanged. The batch editor logged an unrelated Unity Search indexing exception.

The isolated loopback run used the project's FishNet Tugboat transport. Steam matchmaking, other vehicle types, and a full delivery mission remain unverified. The animation review used discrete poses. Clearance between those samples remains unverified.

## Asset authoring

The saved variants, materials, and CRT settings can be edited directly in the Inspector. For a fresh source export, run the editor batch method `CouchGuys.Editor.HoverDeliveryAuthoring.ExportNativeActions`, then run Blender in background mode on the original `CouchGuy_Redesigned.blend` with `Tools/HoverDelivery/build.py -- 4`. The batch method `CouchGuys.Editor.HoverDeliveryAuthoring.Validate` writes a fresh clip and binding inventory under `Logs/HoverDelivery`.
