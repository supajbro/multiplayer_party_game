# Dispatcher idle banter

The saved DeliveryNPC prefab has DispatcherIdleBanter. Its Bubble Prefab references Assets/_Project/Prefabs/UI/DispatcherSpeechBubble.prefab. Edit the saved bubble's Content children to change the cream/navy panel, tail, shadow and TextMeshPro label; no authoring tool needs to be rerun.

Inspector settings on DeliveryNPC:
- Interval Seconds: random minimum/maximum cooldown after each bubble, after dialogue ends, and after returning to view (default 25-50 seconds; clamped to at least one second).
- Display Seconds: total visibility including fade (default 3.5).
- Fade Seconds: pop/fade duration at each end (default 0.2).
- Maximum Distance: camera range in metres (default 12).
- Anchor Offset: local position of the pointer over the TV (default 0, 2.24, 0).
- Lines: editable quips. Changes are picked up on the next spawn/Play session.

Every distinct nonblank line plays once per shuffled rotation; the first line in a new rotation cannot equal the previous line. Duplicate entries are compared ignoring case and surrounding whitespace. A single unique line necessarily repeats after its cooldown; an empty list stays silent.

Banter is cosmetic and local to each client. It reuses existing synchronized dialogue activity to dismiss immediately and restart the cooldown after conversations. Clients may show different idle lines. It never sends additional network messages. One saved UI prefab instance is created lazily and reused; no runtime hierarchy builders, new cameras, textures, or per-frame allocations. Camera/range/frustum checks run twice per second. The UI does not block clicks and retains normal world-space depth occlusion. The screen speaks while its bubble is visible.

Default quips:
1. These couches won't deliver themselves.
2. When's knock off time.
3. I hate this job.
4. I'm on a roll. Legally, that's walking.
5. Pivot. That's the entire training manual.
6. Another day, another mystery cushion stain.
7. Please stop calling it a couch potato. That's management.
8. My dream job? Standby mode.
9. The customer said 'just one flight of stairs'. They always do.
10. I've found more coins in couches than in my pay packet.

Validation: standalone C# tests passed 30,000 draws over 100 seeds, checking full rotations, boundary repeats, sanitizing duplicates/blanks, empty lists and single-line lists. Unity compiled the runtime scripts and imported the saved bubble prefab. The optional Couch Guys > Dispatcher > Validate Idle Banter menu checks the saved references and view show/fade/hide behavior without saving changes. Gameplay presentation and multi-client interactions remain untested because Editor UI control timed out.

Readability update: bubble enlarged to 520 x 164 design units, fixed bold 32-point text with no automatic shrinking, and distance compensation capped at 3x. Configure Minimum Screen Height (relative to a 1080-pixel viewport; default 135) and Maximum Scale Multiplier on DispatcherSpeechBubble > DispatcherBubbleView. The root stays anchored to the TV pointer while the saved child hierarchy remains editable.
