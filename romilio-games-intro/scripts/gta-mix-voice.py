import pathlib,subprocess
import imageio_ffmpeg
root=pathlib.Path(__file__).resolve().parents[1]
p=root/'public/gta6'
# Speech-driven compression ducks the music, with a short attack and gentle recovery.
graph='[1:a]asplit=2[voice][side];[0:a]volume=0.55[music];[music][side]sidechaincompress=threshold=0.018:ratio=5:attack=8:release=190[duck];[duck][voice]amix=inputs=2:normalize=0,alimiter=limit=0.94:level=0,apad[mix]'
subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-y','-v','error','-i',str(p/'voiced-score.wav'),'-i',str(p/'voiceover.wav'),'-filter_complex',graph,'-map','[mix]','-t','60','-ar','48000','-ac','2',str(p/'voiced-mix.wav')],check=True)
print('Mixed 60-second narration and sidechain-ducked score.')
