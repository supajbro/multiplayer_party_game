import json,os
ROOT=r'C:/Users/Jaden/Documents/MyUnityProjects/multiplayer_party_game'
out=os.path.join(ROOT,'ArtSource/Vehicles/Sled4')
with open(os.path.join(ROOT,'ArtSource/Vehicles/NPCTraffic/Workflows/NPC_UtilityPickup.json')) as f:w=json.load(f)
w['122']['inputs']['image']='Sled4_ReferenceCrop.png'
w['322']['inputs']['filename_prefix']='Sled4/Base'
w['401']['inputs']['filename_prefix']='Sled4/BasePreview'
w['402']['inputs']['filename_prefix']='Sled4/GenerationInput'
with open(os.path.join(out,'Sled4_ComfyWorkflow.json'),'w') as f:json.dump(w,f,indent=2)
print('Prepared reference crop and Comfy workflow')
