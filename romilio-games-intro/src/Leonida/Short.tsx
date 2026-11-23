import {AbsoluteFill,Audio,Composition,Folder,Sequence,staticFile,useCurrentFrame,useVideoConfig,interpolate} from 'remotion';
import captions from './captions.json';
import cues from './cues.json';
import {Hook} from './Hook';
import {Setup} from './Setup';
import {Dimez} from './Dimez';
import {Cal} from './Cal';
import {Mirror} from './Mirror';
import {Audience} from './Audience';
import {Ending} from './Ending';
import {pink,cream,cyan,clamp} from './Design';

const Captions=()=>{const f=useCurrentFrame();const {fps}=useVideoConfig();const ms=f/fps*1000;const c=captions.find(x=>ms>=x.startMs&&ms<x.endMs);if(!c)return null;return <div style={{position:'absolute',left:80,right:150,bottom:245,textAlign:'center',display:'flex',justifyContent:'center'}}><div style={{fontFamily:'Arial,sans-serif',fontSize:55,fontWeight:900,lineHeight:1.2,color:cream,padding:'22px 28px',borderRadius:20,background:'#080d1bf0',boxShadow:'0 10px 35px #0008',border:'1px solid #ffffff25',scale:interpolate(ms,[c.startMs,c.startMs+100],[.96,1],clamp)}}>{c.words.map((w,i)=><span key={i} style={{color:ms>=w.startMs&&ms<w.endMs+45?pink:cream,textShadow:ms>=w.startMs&&ms<w.endMs+45?'0 0 25px #ff6bba55':'none'}}>{w.text}{i<c.words.length-1?' ':''}</span>)}</div></div>};

export const LeonidaShort=()=>{const f=useCurrentFrame();const {fps}=useVideoConfig();const cuts=[0,cues.setup,cues.dimez,cues.cal,cues.interpretation,cues.audience,cues.ending];const delta=Math.min(...cuts.filter(c=>f>=c).map(c=>f-c));return <AbsoluteFill style={{background:'#080d1b'}}>
<Sequence name="Hook / the phone" durationInFrames={cues.setup} premountFor={fps}><Hook/></Sequence>
<Sequence name="Setup / life becomes content" from={cues.setup} durationInFrames={cues.dimez-cues.setup} premountFor={fps}><Setup/></Sequence>
<Sequence name="Real Dimez / attention economy" from={cues.dimez} durationInFrames={cues.cal-cues.dimez} premountFor={fps}><Dimez/></Sequence>
<Sequence name="Cal / the other side" from={cues.cal} durationInFrames={cues.interpretation-cues.cal} premountFor={fps}><Cal/></Sequence>
<Sequence name="Interpretation / the same screen" from={cues.interpretation} durationInFrames={cues.audience-cues.interpretation} premountFor={fps}><Mirror/></Sequence>
<Sequence name="Payoff / we keep scrolling" from={cues.audience} durationInFrames={cues.ending-cues.audience} premountFor={fps}><Audience/></Sequence>
<Sequence name="Ending / who is the punchline" from={cues.ending} durationInFrames={1740-cues.ending} premountFor={fps}><Ending/></Sequence>
<Audio src={staticFile('leonida/final-mix.wav')}/>
<AbsoluteFill style={{pointerEvents:'none',background:'repeating-linear-gradient(0deg,transparent 0px,transparent 5px,#0000000c 6px)',opacity:.5}}/>
{delta<7&&<AbsoluteFill style={{background:delta%2?pink:cyan,opacity:(1-delta/7)*.18,translate:`${delta%2?12:-12}px 0`,mixBlendMode:'screen'}}/>}
<Captions/>
<div style={{position:'absolute',top:190,left:80,height:3,width:850,background:'#ffffff20'}}><div style={{height:'100%',background:`linear-gradient(90deg,${cyan},${pink})`,width:`${f/1739*100}%`}}/></div>
<div style={{position:'absolute',left:80,bottom:170,color:'#b3b6c7',font:'20px Arial',letterSpacing:2}}>ORIGINAL ANIMATION / FAN COMMENTARY</div>
</AbsoluteFill>};

export const LeonidaCompositions=()=> <>
<Composition id="GTA6-Punchline" component={LeonidaShort} durationInFrames={1740} fps={30} width={1080} height={1920}/>
<Folder name="Leonida-Scenes">
<Composition id="Leonida-Hook" component={Hook} durationInFrames={cues.setup} fps={30} width={1080} height={1920}/>
<Composition id="Leonida-Setup" component={Setup} durationInFrames={cues.dimez-cues.setup} fps={30} width={1080} height={1920}/>
<Composition id="Leonida-Dimez" component={Dimez} durationInFrames={cues.cal-cues.dimez} fps={30} width={1080} height={1920}/>
<Composition id="Leonida-Cal" component={Cal} durationInFrames={cues.interpretation-cues.cal} fps={30} width={1080} height={1920}/>
<Composition id="Leonida-Mirror" component={Mirror} durationInFrames={cues.audience-cues.interpretation} fps={30} width={1080} height={1920}/>
<Composition id="Leonida-Audience" component={Audience} durationInFrames={cues.ending-cues.audience} fps={30} width={1080} height={1920}/>
<Composition id="Leonida-Ending" component={Ending} durationInFrames={1740-cues.ending} fps={30} width={1080} height={1920}/>
</Folder>
</>;

