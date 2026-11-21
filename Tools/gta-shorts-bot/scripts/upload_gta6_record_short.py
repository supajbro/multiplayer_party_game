"""Upload the explicitly requested original Remotion Short, private and idempotent."""
import argparse
import json
from pathlib import Path
from googleapiclient.http import MediaFileUpload
from shorts.core import load, digest, save_json, now, pipeline_lock
from shorts import youtube, media

ROOT = Path(__file__).resolve().parents[3]
VIDEO = ROOT / 'romilio-games-intro/out/gta6/GTA6RecordShort-voiced.mp4'
MARKER = 'manual-gta6-record-short-voiced-20261122'
TITLE = 'GTA 6 Just Broke a Record That Sounds FAKE #Shorts'
DESCRIPTION = '''GTA VI's reported launch numbers are staggering: $3.79 billion in estimated day-one worldwide retail sales and 32.5 million pre-release copies, according to Take-Two / Rockstar.

Avatar's approximately $2.92 billion is lifetime worldwide theatrical box office. Game retail sales and movie ticket receipts are different measures. Day-one game sales include preorders; these figures are company estimates, not profit.

$3.79B divided by 1,440 minutes is approximately $2.63M per minute: a calculated average, not a live revenue measurement. The biggest-entertainment-launch claim is attributed to Rockstar.

Sources:
https://www.sec.gov/Archives/edgar/data/946581/000162828026075217/exhibit9918k.htm
https://ir.take2games.com/node/16191
https://www.boxofficemojo.com/chart/ww_top_lifetime_gross/

Original procedural animation, original synthesized music and effects, and AI-generated narration. No gameplay or movie footage is used. Fan-made editorial video; no affiliation or endorsement implied.

#GTA6 #GTAVI #Gaming #Shorts'''

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--upload',action='store_true')
    args=parser.parse_args()
    cfg=load(Path(__file__).resolve().parents[1]/'config.json')
    if not cfg.expected_channel_id:raise ValueError('Destination channel is not configured')
    if not VIDEO.is_file():raise ValueError('Narrated video is missing')
    sha=digest(VIDEO)
    info=media.probe(cfg,VIDEO)
    v=next(s for s in info['streams'] if s['codec_type']=='video')
    if (v['width'],v['height'])!=(1080,1920) or abs(float(info['format']['duration'])-60)>.1:
        raise ValueError('Unexpected video dimensions or duration')
    qc=json.loads((VIDEO.parent/'verification.json').read_text())
    if qc['fullDecode']!='passed' or 'Andrew' not in qc['narration']:
        raise ValueError('Narrated render verification is missing')
    folder=cfg.data_dir/'manual-uploads'/MARKER
    folder.mkdir(parents=True,exist_ok=True)
    state_path=folder/'youtube-upload-state.json'
    with pipeline_lock(cfg.data_dir):
        client=youtube.service(cfg)
        channels=client.channels().list(part='snippet,contentDetails',mine=True).execute().get('items',[])
        if len(channels)!=1 or channels[0]['id']!=cfg.expected_channel_id:
            raise ValueError('OAuth destination does not match configured channel')
        channel=channels[0]
        print(json.dumps({'channel':channel['snippet']['title'],'channel_id':channel['id'],'title':TITLE,'privacy':'private','bytes':VIDEO.stat().st_size,'sha256':sha}),flush=True)
        if state_path.exists():
            state=json.loads(state_path.read_text())
            if state.get('sha256')!=sha:raise ValueError('Existing upload state is for different media')
            if not state.get('video_id'):raise ValueError('Prior outcome uncertain; reconcile before retrying')
        else:
            for p in cfg.data_dir.rglob('*upload*.json'):
                prior=json.loads(p.read_text(encoding='utf-8-sig'))
                if prior.get('sha256')==sha:
                    raise ValueError(f'Matching upload record already exists: {p.name}')
            playlist=channel['contentDetails']['relatedPlaylists']['uploads']
            token=None
            for _ in range(2):
                page=client.playlistItems().list(part='contentDetails',playlistId=playlist,maxResults=50,pageToken=token).execute()
                ids=[x['contentDetails']['videoId'] for x in page.get('items',[])]
                if ids:
                    items=client.videos().list(part='snippet',id=','.join(ids)).execute().get('items',[])
                    for item in items:
                        desc=item['snippet'].get('description','')
                        if MARKER in desc or sha in desc:raise ValueError('Matching remote video exists; reconcile instead of inserting')
                token=page.get('nextPageToken')
                if not token:break
            description=DESCRIPTION+f'\n\nProduction: {MARKER}\nSHA256: {sha}'
            manifest={'video':str(VIDEO),'sha256':sha,'title':TITLE,'description':description,'privacy':'private','user_requested_upload':True,'review':'Original procedural visuals and synthesized score; generated generic neural narration; no third-party footage. Source notes retained beside the render.','qc':qc,'channel_id':channel['id'],'prepared':now()}
            save_json(folder/'manifest.json',manifest)
            (folder/'youtube-description.txt').write_text(description,encoding='utf-8')
            if not args.upload:
                print('READY_TO_UPLOAD',flush=True)
                return
            save_json(state_path,{'status':'uploading','started':now(),'sha256':sha,'channel_id':channel['id']})
            try:
                request=client.videos().insert(part='snippet,status',body={
                    'snippet':{'title':TITLE,'description':description,'categoryId':'20','tags':['GTA 6','GTA VI','gaming','launch records','Shorts']},
                    'status':{'privacyStatus':'private','selfDeclaredMadeForKids':False,'containsSyntheticMedia':True}},
                    media_body=MediaFileUpload(str(VIDEO),mimetype='video/mp4',chunksize=8*1024*1024,resumable=True))
                response=None
                while response is None:
                    progress,response=request.next_chunk(num_retries=3)
                    if progress:print(f'Upload {progress.progress():.0%}',flush=True)
                state={'status':'uploaded','video_id':response['id'],'url':'https://www.youtube.com/watch?v='+response['id'],'privacy':'private','sha256':sha,'channel_id':channel['id'],'time':now()}
                save_json(state_path,state)
            except Exception:
                save_json(state_path,{'status':'upload_uncertain','sha256':sha,'channel_id':channel['id'],'time':now()})
                raise RuntimeError('Remote outcome uncertain; inspect channel before retrying') from None
        remote=client.videos().list(part='snippet,status,processingDetails',id=state['video_id']).execute().get('items',[])
        if not remote or remote[0]['snippet']['channelId']!=cfg.expected_channel_id or remote[0]['status']['privacyStatus']!='private':
            raise ValueError('Remote video verification failed')
        item=remote[0]
        verification={'checked':now(),'video_id':item['id'],'title':item['snippet']['title'],'channel':channel['snippet']['title'],'privacy':item['status']['privacyStatus'],'upload_status':item['status'].get('uploadStatus'),'processing':item.get('processingDetails',{}).get('processingStatus')}
        save_json(folder/'upload-verification.json',verification)
        print(json.dumps({'upload':state,'verification':verification}),flush=True)

if __name__=='__main__':main()
