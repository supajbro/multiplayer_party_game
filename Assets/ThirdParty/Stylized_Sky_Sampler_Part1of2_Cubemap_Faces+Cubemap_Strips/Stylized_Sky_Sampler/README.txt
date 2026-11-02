STYLIZED SKY SAMPLER
====================
by Munchkin Gaming - https://munchkin-jonesy.itch.io

Five free stylized HD skyboxes with clean cel-shaded clouds: a sunny day, a sunset, golden hour, a moonlit night and an aurora. 4K, smooth, anime-style.

SKIES (5)
  01  Sunny Day
  02  Sunset
  03  Golden Hour
  04  Moonlit Night
  05  Aurora


WHAT'S INSIDE
-------------
  Equirect_4096x2048/   every sky as a 4K 2:1 panorama
  Equirect_2048x1024/   2K copies for lighter builds and mobile
  Cubemap_1024/<Sky>/   six 1024x1024 faces: right, left, up, down, front, back
  Cubemap_6x1_Strip/    the six faces in one 6144x1024 strip (+X -X +Y -Y +Z -Z)
  Godot_Demo/           a Godot 4 sky viewer (see below)

All images are smooth full-colour PNG with cel-shaded clouds.
Every sky is seamless all the way round. The lower half is a plain ground/fog
tone, so it works under a floor or a far terrain.


HOW TO USE
----------
Use normal (linear/bilinear) filtering and turn compression quality up.

Godot 4:   WorldEnvironment > Environment > Background: Sky >
           Sky Material: PanoramaSkyMaterial, Panorama = an Equirect PNG,
           Filter ON. Set Sky Rotation Y to 180 so the image centre faces -Z.
Unity:     import an Equirect PNG, Texture Shape: Cube, Mapping: Latitude-Longitude,
           Filter Mode: Bilinear. Make a Skybox/Cubemap material with it and set it
           in Lighting > Environment. (Or use the 6x1 strip with Mapping: Auto.)
Unreal:    import an Equirect PNG as a Texture Cube (Filter: Default) and use it
           in a Sky Sphere / Sky Light, or on an inward-facing sphere.
Blender:   World > Environment Texture > Equirect PNG, interpolation: Linear.

Godot_Demo: open project.godot in Godot 4, press F5.
  Left/Right or Q/E  change sky      mouse  look around
  WASD               walk            H      hide the label
The demo lights each sky with a matching sun or moon so you can see how your
level will look under it.


LICENCE
-------
See LICENSE.txt. Use them in personal and commercial projects. Don't resell
or share the skies themselves.

Made with AI assistance (procedural generation scripts written with AI).
