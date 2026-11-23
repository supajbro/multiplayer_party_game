"""Keep the editable master consistent with the finished Unity texture maps."""
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parents[2];SRC=ROOT/'ArtSource/Characters/Hotwire'
bpy.ops.wm.open_mainfile(filepath=str(SRC/'Hotwire.blend'))
material=bpy.data.objects['thief_enemy_01'].data.materials[0]
bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
node=material.node_tree.nodes.new('ShaderNodeTexImage');node.name='Finished Hotwire atlas';node.image=bpy.data.images.load(str(ROOT/'Assets/_Project/Textures/Thieves/Hotwire/Hotwire_BaseColor.png'));node.image.pack()
material.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(SRC/'Hotwire.blend'))
print('HOTWIRE_FINISHED_MATERIAL_PACKED')
