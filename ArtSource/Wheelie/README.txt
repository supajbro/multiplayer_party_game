WHEELIE / COUCH GUYS

Source: Wheelie.blend (editable geometry, rigid skin, studio and camera).
Game model: ../../Assets/_Project/Models/Wheelie/Wheelie.fbx
Materials: same Unity folder, Materials/. Uses the existing Couch Guys toon shader.
No external textures are required. All colour and illumination use material slots.
Front, side, rear and three-quarter PNGs are provided alongside this file.

Scale: 1.132 m including antenna; metre units; root at ground (0,0,0).
Blender front is -Y and up is +Z. FBX uses standard Unity -Z forward/+Y up export.
Wheel bone head is the axle: Blender (0,0,0.2025). Rotate its LOCAL Y to roll.
Chassis carries the fender and suspension independently from the Wheel bone.
Root > Chassis > Waist > Torso > Neck > Head.
Each arm has shoulder, upper arm, forearm, wrist, hand and eight finger bones.
WeaponSocket.L and WeaponSocket.R are grip attachment bones, local +Y forward.
Generic rig, rigid weights, 35 bones, two skinned meshes, five material slots.
27,686 triangles; 14,314 authored vertices (Unity may split at UV/material seams).

FaceDisplay is a separate, blank black mesh with material slot zero.
UVMap matches the existing RobotFaceDisplaySettings region AND rotation:
rect (0.38040865, 0.7526041, 0.23918271, 0.24479163), RotateUV=1.
Screen01 is an optional normalized second UV layer; existing shader uses UVMap.
No expressions, eyes, mouths, scarf, bandana or controller logo are included.
The existing face component can resolve FaceDisplay by name when integrated later.
No gameplay or display components have been added, and the player is unchanged.

Visual review: Compared all four rendered views with the supplied concept.
Preserved single-wheel silhouette, oversized rounded TV head, circular ears,
angled antenna, compact tapered chest, exposed segmented spine, dark joints,
three-finger hands and cyan wheel rings. Clean mechanical chest replaces fabric.
Corrections after first review: closed screen centre, seated rear fender light,
smoothed head shell and reduced small-part bevel geometry by about one third.
Rig deformation and FBX reimport checks are recorded in rig_validation.json.
Unity shader appearance/performance has not been measured in Play mode.

Tools/Wheelie/build_wheelie.py is an explicit offline authoring script, not an
import hook. Edit the saved Blender file directly; rerunning the script replaces
Wheelie source/export files, so preserve manual edits before using it.
