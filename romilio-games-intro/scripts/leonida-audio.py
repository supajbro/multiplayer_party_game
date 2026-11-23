"""Original stereo synth score, designed effects, voice-driven ducking and mastering."""
import json,pathlib,wave,math,random,subprocess,array
import imageio_ffmpeg
root=pathlib.Path(__file__).resolve().parents[1];p=root/'public/leonida'
cues=json.loads((root/'src/Leonida/cues.json').read_text())
sr=48000;duration=58;rng=random.Random(608);pcm=array.array('h')
hits=[0]+[cues[k]/30 for k in ['setup','dimez','cal','interpretation','audience','ending']]
for i in range(sr*duration):
    t=i/sr;b=t*100/60;beat=b%1;bar=int(b/4);freq=[55,65.4064,43.6535,48.9994][bar%4]
    # Minor harmony, soft pluck arpeggio, syncopated bass and restrained drums.
    pad=sum(math.sin(2*math.pi*freq*2*2**(n/12)*t+math.sin(t*.3)*.05) for n in [0,7,10])/3*.045
    bass=math.sin(2*math.pi*freq*t)*math.exp(-beat*7)*.11
    arp=math.sin(2*math.pi*freq*8*2**([0,7,10,14,12,10,7,3][int(b*2)%8]/12)*t)*math.exp(-(b*2%1)*7)*.035
    kick=math.sin(2*math.pi*(48*beat+2*(1-math.exp(-beat*40))))*math.exp(-beat*25)*.17
    hat=rng.uniform(-1,1)*math.exp(-(b*2%1)*60)*.024
    snare=rng.uniform(-1,1)*math.exp(-beat*22)*(.055 if int(b)%4 in [1,3] else 0)
    music=pad+bass+arp+kick+hat+snare
    if t>hits[-1]-.25:music*=.1
    fx=0
    for h in hits:
        d=t-h
        if 0<=d<.65:fx+=(math.sin(2*math.pi*(45*d+1.5*(1-math.exp(-d*25))))*.26+rng.uniform(-1,1)*.06)*math.exp(-d*9)
        lead=h-t
        if 0<lead<.32:fx+=rng.uniform(-1,1)*.06*(1-lead/.32)**2
    # Small UI notification ticks, kept far behind speech.
    for h in [20.3,22.7,26.8,31.2,43.2,51.2]:
        d=t-h
        if 0<=d<.16:fx+=math.sin(2*math.pi*1174*d)*math.exp(-d*35)*.045
    fade=min(1,t*20,(duration-t)*3)
    stereo=math.sin(t*.6)*.08
    for c in [-1,1]:pcm.append(int(max(-.98,min(.98,(music*(1+c*stereo)+fx)*fade))*32767))
with wave.open(str(p/'original-score.wav'),'wb') as w:w.setnchannels(2);w.setsampwidth(2);w.setframerate(sr);w.writeframes(pcm.tobytes())
ff=imageio_ffmpeg.get_ffmpeg_exe()
graph='[1:a]asplit=2[v][side];[0:a]volume=0.65[m];[m][side]sidechaincompress=threshold=0.02:ratio=6:attack=8:release=240[d];[d][v]amix=inputs=2:normalize=0,loudnorm=I=-15:TP=-1.5:LRA=7,alimiter=limit=0.84:level=0[mix]'
subprocess.run([ff,'-y','-v','error','-i',str(p/'original-score.wav'),'-i',str(p/'voiceover.wav'),'-filter_complex',graph,'-map','[mix]','-t','58','-ar','48000','-ac','2',str(p/'final-mix.wav')],check=True)
print('Original score + neural narration: 58 seconds, speech-driven ducking, mastered stereo.')
