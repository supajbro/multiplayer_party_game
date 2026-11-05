# Dispatcher TV menu

The existing `SampleScene` generator uses `Assets/_Project/Prefabs/DeliveryNPC.prefab`.
The saved prefab now references the independent world-space `DispatcherMenuUI` prefab.
There is no additional dispatcher placed in the scene and no runtime UI hierarchy builder.

Edit these assets directly in Unity:

- `Assets/_Project/Prefabs/UI/DispatcherMenuUI.prefab`: three delivery card instances, scrollable shop, funds and feedback.
- `Assets/_Project/Prefabs/UI/DispatcherDeliveryButton.prefab`: whole-card appearance, supplied background/highlight sprites, text, image and layout. Each delivery instance overrides its placeholder sprite.
- `DeliveryNPC` > `DispatcherMenuController`: UI reference, camera focus and screen anchors, 0.6-second transition, FOV, existing button renderers/colliders, face material slot and state colours.
- `DeliveryNPC/CameraFocusPoint` and `ScreenCanvasAnchor`: positions follow the dispatcher, including roaming rotation and translation.

E opens the server-authorised interaction. E or Escape closes it and blends back to the existing orbit camera. The owner motor and input reader stay suppressed until the return blend finishes. Cursor, existing event systems, other UI raycasters and renderer property blocks are restored on closing or teardown.

The physical buttons use direct collider raycasts. Only face material slot 1 changes; the icon and metal slots remain untouched. The screen uses a local black `_WaveColor` property block while the menu is open, preserving the curved screen surface and the existing waveform animation phase.

Delivery/shop option callbacks still call the existing authoritative purchase methods. Funds, prices, rewards, destination eligibility, reservations, item fulfilment and purchase request deduplication remain server-controlled. The former exclusive dispatcher lock is now a separate session per player. Roaming stays paused until the last session ends; tab, camera and UI state are never replicated.

`Couch Guys > Dispatcher > Install In-World TV Menu` creates missing assets only; it does not rebuild existing UI prefabs or overwrite their Inspector customizations. Validation and preview rendering have separate explicit menu commands. No import, script reload or Play-mode callback rebuilds these prefabs.

Validation records and previews are in `Tools/DispatcherMenuValidation.txt`, `Tools/DispatcherMenuRuntimeValidation.txt`, `Tools/DispatcherMenuPreview.png` and `Tools/DispatcherShopPreview.png`.
