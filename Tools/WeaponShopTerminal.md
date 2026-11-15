Weapon shop terminal
====================

Saved prefabs:
- Assets/_Project/Prefabs/UI/WeaponShopTerminal.prefab
- Assets/_Project/Prefabs/UI/WeaponShopTerminalUI.prefab

The terminal is nested in depot_02, 1.05 metres in front of the existing
WeaponShopDisplay, aligned to the display and grounded at the depot floor.
The display and its existing Inspector overrides are preserved.

Press E nearby to focus the local camera on the angled screen. The menu opens
directly to weapons. Scroll through the list and click a card to purchase;
E or Escape closes the menu and restores the camera, movement and cursor.
Unaffordable/unavailable cards use the existing shop feedback. Purchases use
the shared DeliveryManagerShop catalogue, existing fulfilment and team funds.
Weapons are excluded from the dispatcher UI and rejected in dispatcher sessions
on the server; the dispatcher retains its other shop items and deliveries.

Edit the terminal prefab in Prefab Mode:
- WeaponShopTerminal: interaction point, range and prompt.
- DispatcherMenuController: saved UI prefab, camera focus, screen anchor,
  transition time, focus FOV and idle screen. Keep Shop Only enabled.
- CameraFocusPoint: position/rotation reached by the camera during the zoom.
- Hide Local Player While Open: enabled for the terminal so the local body cannot
  obscure the screen. Previous renderer visibility is restored after returning.
- ScreenCanvasAnchor: fitted to the existing screen mesh.
- IdleScreen: editable world-space background, title and hint.

Edit WeaponShopTerminalUI for menu layout and typography. Its card prefab is the
existing DispatcherDeliveryButton; changing that shared card also changes dispatcher
cards. Runtime only instantiates these saved prefabs and binds shop state.

The terminal is a static local prop, not a new network entity. Existing owned-player
RPCs request opening, buying and closing. The server resolves its own terminal marker,
validates distance and session category, and charges only after successful fulfilment.
Camera/UI animation runs locally. A close request carries its session ID so an old
dispatcher close cannot cancel a newer terminal session.
During screen focus, the camera follows CameraFocusPoint exactly, including live
position/rotation edits; player-body orbit clearance resumes on return to gameplay.

Editor menus under Couch Guys > Weapon Terminal:
- Create Missing Prefabs And Place In Depot 02: explicit installation; preserves
  existing terminal/menu assets and existing placement on subsequent runs.
- Validate Prefabs: checks wiring, exclusive category filtering and depot placement.
- Test Host Purchases And Camera: transient Play-mode integration probe; does not
  save scene changes. Results: Temp/WeaponTerminalPlayValidation.txt.

No import/reload/Play-mode asset rebuilding is installed.
