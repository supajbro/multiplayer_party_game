`water_fountain` replaces the old fountain while retaining prefab GUID
`5f073b482531af8439406b2a846e3004` and root file ID `7539993096332369416`.
Both the weighted landmark list and the Fountain library entry in Region_Suburbs
therefore continue to use the replacement. Placement remains 5.5 x 5.5 m with 1 m clearance.

- Prefab: `Assets/_Project/Prefabs/Environment/WaterFountain/water_fountain.prefab`
- Model: `Assets/_Project/Models/Environment/WaterFountain/water_fountain.fbx`
- Blender source: `Tools/Blender/SourceModels/water_fountain.blend`
- Mesh LOD triangles: 19,560 / 10,320 / 1,360, plus the shared water geometry.
- LOD screen heights: 22% / 10% / 1.2%, with crossfades.
- Effects: full inside 28 m, 30% emission until 48 m, disabled outside distance/frustum.
  CullingGroup distances are measured from the effect bounding sphere surface.
- Seven static box colliders cover the foundation, walls, pedestal and upper bowl.
  Water and particles have no physical collisions or network components.

Edit materials, shader properties, native particle modules, collider sizes and
FountainWaterEffects settings directly in the Inspector. The optional Camera
reference overrides Camera.main. No runtime model construction or automatic
asset rebuilding occurs. The creation menu preserves an existing saved prefab.
Tools > Couch Guys > Water Fountain > Validate and Render checks the saved asset
and renders a preview without changing the open scene.

Validation used Unity 6000.3.11f1 and the project's installed URP 17.3 packages
in an isolated project, leaving the open scene untouched. See the PNG and two
validation reports beside this file. Play-mode checks exercised two fountain
instances, distance/frustum transitions, reactivation, camera replacement,
upward/falling particles, splash/ripple simulation, and water image changes over
time. Full neighbourhood gameplay was not launched; spawn integration was checked
by preserving and validating the region's serialized references and placement
contract, and instantiating the saved prefab in Unity.
