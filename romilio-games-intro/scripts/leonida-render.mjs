import {bundle} from '@remotion/bundler';
import {selectComposition,renderStill,renderMedia} from '@remotion/renderer';
import {mkdirSync,writeFileSync} from 'node:fs';
const serveUrl=await bundle({entryPoint:'src/index.ts'});
const composition=await selectComposition({serveUrl,id:'GTA6-Punchline'});
mkdirSync('out/leonida/checks',{recursive:true});
for(const frame of [35,220,440,620,790,960,1150,1290,1450,1570,1695]){
 await renderStill({serveUrl,composition,frame,imageFormat:'png',output:`out/leonida/checks/${frame}.png`});
}
console.log('STILLS_READY');
if(!process.argv.includes('--stills')){
 let last=-1;
 await renderMedia({serveUrl,composition,codec:'h264',crf:17,pixelFormat:'yuv420p',audioCodec:'aac',audioBitrate:'320k',concurrency:4,outputLocation:'out/leonida/GTA6-Punchline.mp4',onProgress:({progress})=>{const n=Math.floor(progress*20);if(n!==last){console.log(`Render ${n*5}%`);last=n;}}});
 writeFileSync('out/leonida/render-metadata.json',JSON.stringify({id:composition.id,width:composition.width,height:composition.height,fps:composition.fps,durationInFrames:composition.durationInFrames,durationSeconds:58,codec:'H264',audio:'AAC stereo 320 kbps',visuals:'Original animated SVG/CSS compositions. No Rockstar footage, screenshots or music.'},null,2));
 console.log('RENDER_COMPLETE');
}
