COUCH GUYS — CARTOON ASSAULT RIFLE — ONE-SHOT SFX PACK
=======================================================
Fire audio is NON-LOOPING. Each of the SIX Fire WAV files contains EXACTLY
ONE rifle gunshot, with its own short natural tail. NO multiple shots baked in.
The actual rate of fire comes exclusively from your existing Unity weapon code.

GAMEPLAY AUDIO:
  CouchGuys_AR_Fire_01_Punchy.wav   - punchy and full
  CouchGuys_AR_Fire_02_Crisp.wav    - bright transient and tight tail
  CouchGuys_AR_Fire_03_Thumpy.wav  - a little heavier in the low end
  CouchGuys_AR_Fire_04_Bright.wav  - brisk and sparkly
  CouchGuys_AR_Fire_05_Tight.wav   - compact, fast attack
  CouchGuys_AR_Fire_06_Arcade.wav  - exaggerated, playful punch
  CouchGuys_AR_FiringStop_MechanicalClack.wav - one standalone release cue

FOR AUDITION ONLY:
  DEMO_ONLY_AutomaticBurst_600RPM.wav - example stream of single shots,
                                      plus release click; DO NOT import as a loop.

All original synthesised effects; 48,000 Hz, 24-bit PCM, mono WAV.

UNITY INTEGRATION NOTES:
  * Set looping OFF: AudioSource.loop = false.
  * Every time a BULLET FIRES, choose a random Fire clip, and PlayOneShot(clip).
    Avoid immediately repeating the same variation if convenient.
  * For automatic weapons, let the current gun fire-rate/cooldown drive shots.
    This pack does NOT impose a rate of fire (600 RPM is DEMO ONLY).
  * Use the stop clack ONCE when a sustained firing burst ends, e.g. player
    releases the trigger, swaps weapons, reloads, dies, or runs out of ammo.
    Do not play it when no shot was fired. It is not a replacement for the tail.
  * Consider a dedicated AudioSource for the stop clack if its playback must
    remain independent of overlapping single-shot sounds.
  * Fire one-shots may overlap naturally; avoid enforcing a cut when the next
    bullet fires. If needed, apply a small amount of pitch randomisation
    (0.98-1.02). Preserve each shot's attack by not restarting the same source.
  * Only fire on actual weapon shot events, not every Update frame.
  * Multiplayer: ensure other players hear shots once, without doubling locally.
  * The included DEMO is NOT a gameplay clip and must never be looped.

This REPLACES the previous fixed-length assault rifle firing LOOP pack.
