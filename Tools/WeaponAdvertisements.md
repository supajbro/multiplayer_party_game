Weapon advertisement TV
=======================

Drag `Assets/_Project/Prefabs/UI/WeaponShopDisplay.prefab` into your depot prefab.
It includes the existing weapon_shop_display FBX and a fitted World Space Canvas.
No scene or depot placement is included.

On the root WeaponAdvertisementDisplay component:

- Catalogue references the existing DeliveryManagerShop asset. Every distinct Weapon
  category entry with a purchase behaviour is included automatically, in catalogue order.
  Add weapons to that shop catalogue as usual; do not maintain another advertisement list.
- Scroll Interval defaults to 5 seconds between slide starts, including transition time.
- Transition Duration defaults to 0.65 seconds and is clamped to the interval at runtime.
- The two panels, typography, colours, placeholder labels and canvas are editable in
  Prefab Mode. Runtime code only updates text, panel visibility and horizontal positions.

This is advertising only: no raycaster, buttons, colliders, EventSystem, purchase calls,
NetworkObject, network behaviours or RPCs. Each client uses its own unscaled frame time.
An empty catalogue hides ads; one weapon stays stationary. Names and prices are read
from the shared item assets when binding an ad. The catalogue is refreshed every cycle.

Use `Couch Guys > Weapon Advertising > Validate TV Prefab` to run deterministic Editor
checks in an isolated preview scene. Results and a rendered preview are written to
`Temp/WeaponAdvertisementValidation.txt` and `Temp/WeaponAdvertisementPreview.png`.
Checks cover two complete loops, names/prices, transition midpoint spacing, wraparound,
re-enable, runtime catalogue addition, changed price, empty/single-item catalogues and
zero-duration transitions. Validation clones catalogue data and never changes shop assets.

`Create Missing TV Prefab` is an explicit authoring command and refuses to overwrite an
existing prefab. There is no automatic import, reload or Play mode rebuilding.
