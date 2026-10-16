COUCH GUYS — CARTOON SHOTGUN: ONE-SHOT SOUND PACK
================================================
CORRECTED EDITION — ONE gunshot per Fire WAV. No audio loops or repeated blasts.
Each shooting WAV contains exactly one cartoon shotgun blast and its natural decay.

Fire variations:
  CouchGuys_Shotgun_Fire_01_Chunky.wav       (0.73s) heavy/compressed punch
  CouchGuys_Shotgun_Fire_02_Snappy.wav       (0.61s) brisk and bright
  CouchGuys_Shotgun_Fire_03_Thunderous.wav  (0.92s) large warm boom
  CouchGuys_Shotgun_Fire_04_Arcade.wav       (0.67s) bold stylised pop

Optional extra:
  CouchGuys_Shotgun_Pump_Separate.wav       (0.39s) pump-action noise ONLY

Audio: 48,000 Hz, 24-bit PCM, mono WAV. Original synthesised effects.

HOW TO PLAY IN UNITY:
  - AudioSource.loop = false. DO NOT set AudioSource.loop = true.
  - On EACH real shotgun shot, call AudioSource.PlayOneShot(randomFireClip).
  - The game's weapon firing/cooldown logic controls when the NEXT shot happens.
  - Do not restart/loop the audio directly, and do not change weapon fire rate.
  - Optionally trigger the separate pump sound on the pump animation event.
  - For multiplayer, avoid double-playing the local shot when replicating remote SFX.

Unlike the original incorrectly looped pack, these files do NOT impose 60 RPM.
They suit any shotgun firing cadence determined by Unity gameplay logic.
