// Explicit Shader Graph authoring from Unity's installed URP template. No import hooks.
const fs = require('fs');
const crypto = require('crypto');
const packageName = fs.readdirSync('Library/PackageCache').find(x=>x.startsWith('com.unity.shadergraph@'));
if(!packageName) throw new Error('Import the project Shader Graph package first.');
const base = 'Library/PackageCache/'+packageName+'/';
const read = p => fs.readFileSync(base+p,'utf8').trim().split(/\r?\n\s*\r?\n(?=\{)/).map(JSON.parse);
const template = read('GraphTemplates/Cross Pipeline/Unlit Simple.shadergraph');
const custom = read('Samples~/NodeReference/Utility/CustomFunction.shadergraph');
const clone = x => JSON.parse(JSON.stringify(x));
const id = () => crypto.randomUUID().replaceAll('-','');
const ref = x => ({m_Id: typeof x === 'string' ? x : x.m_ObjectId});
const graph = clone(template.find(x=>x.m_Type.endsWith('.GraphData')));
const target = clone(template.find(x=>x.m_Type.endsWith('.UniversalTarget')));
const sub = clone(template.find(x=>x.m_Type.endsWith('.UniversalUnlitSubTarget')));
target.m_CastShadows=false; target.m_ReceiveShadows=false; target.m_RenderFace=0;
graph.m_Path='CouchGuys/Environment'; graph.m_ActiveTargets=[ref(target)];
graph.m_Properties=[]; graph.m_CategoryData=[]; graph.m_Nodes=[]; graph.m_Edges=[];
const objects=[graph,target,sub];
function add(x){ objects.push(x); return x; }
function node(x){ add(x); graph.m_Nodes.push(ref(x)); return x; }
function edge(a,as,b,bs){ graph.m_Edges.push({m_OutputSlot:{m_Node:ref(a),m_SlotId:as},m_InputSlot:{m_Node:ref(b),m_SlotId:bs}}); }
const blocks=template.filter(x=>x.m_Type.endsWith('.BlockNode') && /VertexDescription|BaseColor/.test(x.m_Name));
for(const b of blocks){ node(clone(b)); for(const s of b.m_Slots)add(clone(template.find(x=>x.m_ObjectId===s.m_Id))); }
graph.m_VertexContext.m_Blocks=blocks.filter(x=>x.m_Name.startsWith('Vertex')).map(ref);
graph.m_FragmentContext.m_Blocks=blocks.filter(x=>x.m_Name.endsWith('BaseColor')).map(ref);
graph.m_VertexContext.m_Position={x:900,y:0}; graph.m_FragmentContext.m_Position={x:900,y:230};
const fn=clone(custom.find(x=>x.m_Type.endsWith('.CustomFunctionNode') && x.m_FunctionName==='Simple'));
fn.m_ObjectId=id(); fn.m_Group=ref(''); fn.m_Name='Stylized fountain waves'; fn.m_FunctionName='FountainWaves'; fn.m_Slots=[];
fn.m_DrawState.m_Position={serializedVersion:'2',x:350,y:0,width:340,height:500}; fn.m_PreviewExpanded=true;
fn.m_FunctionBody=`
float t = _Time.y * WaveSpeed;
float2 p = Position.xz;
Displaced = Position;
Displaced.y += sin(p.x*3.1+t)*cos(p.y*2.7-t)*WaveIntensity;
float2 q = p*PatternScale + float2(t*.3,-t*.2);
q += .32*float2(sin(q.y*1.6+t),cos(q.x*1.3-t));
float cells = sin(q.x+sin(q.y))*cos(q.y+sin(q.x*.8));
float broad = .5+.5*sin(p.x*2+p.y*1.7+t*.6);
float veins = 1-smoothstep(.035,.16,abs(cells-.14));
float radius = length(p);
float ring = pow(.5+.5*sin(radius*14-t*3.8+sin(p.x*2+t)*.35),10)*RippleStrength*smoothstep(.5,.95,radius);
float edge = smoothstep(2.02,2.18,max(abs(p.x),abs(p.y)))*EdgeFoam;
Color = lerp(BaseColor,ShallowColor,saturate(.4+cells*.36+broad*.25));
Color = lerp(Color,HighlightColor,saturate(veins*.33+ring*.65+edge));`;
node(fn);
const vectorSlot=custom.find(x=>x.m_Type.endsWith('.Vector3MaterialSlot'));
function slot(index,name,type='Vector3MaterialSlot',output=false){
    const s=clone(vectorSlot); s.m_ObjectId=id(); s.m_Id=index; s.m_DisplayName=name; s.m_ShaderOutputName=name;
    s.m_Type='UnityEditor.ShaderGraph.'+type; s.m_SlotType=output?1:0; s.m_StageCapability=3;
    if(type==='Vector1MaterialSlot'){s.m_Value=0; s.m_DefaultValue=0;}
    if(type==='PositionMaterialSlot')s.m_Space=0;
    add(s); fn.m_Slots.push(ref(s)); return s;
}
slot(0,'Position');
const position=clone(template.find(x=>x.m_Type.endsWith('.PropertyNode')));
position.m_ObjectId=id(); position.m_Type='UnityEditor.ShaderGraph.PositionNode'; position.m_SGVersion=1;
delete position.m_Property; position.m_Name='Object position'; position.m_Space=0; position.m_PositionSource=0;
position.m_DrawState.m_Position={serializedVersion:'2',x:-200,y:-140,width:180,height:100};
const positionSlot=clone(vectorSlot); positionSlot.m_ObjectId=id(); positionSlot.m_Id=0;
positionSlot.m_DisplayName='Out'; positionSlot.m_ShaderOutputName='Out'; positionSlot.m_SlotType=1;
add(positionSlot); position.m_Slots=[ref(positionSlot)]; node(position); edge(position,0,fn,0);
const props=[
    ['BaseColor','Deep cyan',[.005,.38,.65,1]],['ShallowColor','Bright cyan',[.015,.76,.95,1]],
    ['HighlightColor','Ripple highlights',[.35,.96,1,1]],['WaveSpeed','Wave speed',.7,[0,3]],
    ['WaveIntensity','Surface distortion',.008,[0,.04]],['RippleStrength','Circular ripple strength',.55,[0,1]],
    ['PatternScale','Wave pattern scale',4,[1,12]],['EdgeFoam','Basin edge highlight',.35,[0,1]]];
const propTemplate=template.find(x=>x.m_Type.endsWith('.ColorShaderProperty'));
const pnTemplate=template.find(x=>x.m_Type.endsWith('.PropertyNode'));
props.forEach(([name,label,value,range],i)=>{
    const color=Array.isArray(value); const p=clone(propTemplate); p.m_ObjectId=id(); p.m_Guid.m_GuidSerialized=crypto.randomUUID();
    p.m_Name=label; p.m_DefaultReferenceName='_'+name; p.m_OverrideReferenceName='_'+name;
    p.m_RefNameGeneratedByDisplayName=label;
    if(color) p.m_Value={r:value[0],g:value[1],b:value[2],a:value[3]};
    else { p.m_Type='UnityEditor.ShaderGraph.Internal.Vector1ShaderProperty'; p.m_Value=value; p.m_FloatType=1; p.m_RangeValues={x:range[0],y:range[1]}; }
    add(p); graph.m_Properties.push(ref(p));
    const pn=clone(pnTemplate); pn.m_ObjectId=id(); pn.m_Property=ref(p); pn.m_DrawState.m_Position={serializedVersion:'2',x:-200,y:i*100,width:210,height:40};
    const s=clone(vectorSlot); s.m_ObjectId=id(); s.m_Id=0; s.m_DisplayName=label; s.m_ShaderOutputName='Out'; s.m_SlotType=1;
    s.m_Type='UnityEditor.ShaderGraph.'+(color?'Vector4MaterialSlot':'Vector1MaterialSlot');
    s.m_Value=color?{x:0,y:0,z:0,w:0}:0; s.m_DefaultValue=clone(s.m_Value); add(s); pn.m_Slots=[ref(s)]; node(pn);
    slot(i+1,name,color?'Vector3MaterialSlot':'Vector1MaterialSlot'); edge(pn,0,fn,i+1);
});
slot(9,'Displaced','Vector3MaterialSlot',true); slot(10,'Color','Vector3MaterialSlot',true);
edge(fn,9,blocks.find(x=>x.m_Name==='VertexDescription.Position'),0);
edge(fn,10,blocks.find(x=>x.m_Name==='SurfaceDescription.BaseColor'),0);
fs.writeFileSync('Assets/_Project/Shaders/FountainWater.shadergraph',objects.map(x=>JSON.stringify(x,null,4)).join('\n\n')+'\n');
console.log('Saved FountainWater.shadergraph');
