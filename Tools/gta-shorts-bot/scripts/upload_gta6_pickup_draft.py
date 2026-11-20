"""Explicitly requested private review upload of ONE pickup cut; never publishes."""
import json
from pathlib import Path
from googleapiclient.http import MediaFileUpload
from shorts.core import load, digest, save_json, now, pipeline_lock
from shorts import youtube, media

FOLDER = 'gta6-meme-test/pickup-draft-20261119-201346'
MARKER = 'manual-gta6-pickup-20261119-201346'

def main():
    cfg = load(Path('config.json'))
    folder = cfg.data_dir / FOLDER
    m = json.loads((folder/'manifest.json').read_text(encoding='utf-8'))
    output = Path(m['output'])
    if digest(output) != m['sha256'] or digest(Path(m['source'])) != m['source_sha256']:
        raise ValueError('Reviewed media changed')
    rules = folder/'evidence/rules-current.txt'
    if not all(x in rules.read_text(encoding='utf-8-sig') for x in ['YouTube Shorts','#KaiCenat','Kai Cenat']):
        raise ValueError('Missing source permission evidence')
    qc = json.loads((folder/'audio-picture-qc.json').read_text())
    if qc['decode_exit'] or qc['silence_events'] or qc['black_events'] or float(qc['peak_db'][0]) > -.1:
        raise ValueError('Technical QC failed')
    info = media.probe(cfg,output)
    v = next(s for s in info['streams'] if s['codec_type']=='video')
    if (v['width'],v['height']) != (1080,1920) or not 22<float(info['format']['duration'])<23:
        raise ValueError('Unexpected reviewed file')
    description = (
        'Kai Cenat tries GTA VI budget transport. Seatbelt sold separately. #KaiCenat #GTA6 #Shorts\n\n'
        'Gameplay and reactions: Kai Cenat (@KaiCenat). Fan-made editorial meme edit.\n'
        'Creator: https://www.twitch.tv/kaicenat\n'
        'Source: https://archive.org/details/kai-cenats-first-time-playing-gta-6\n'
        'Action selected from 01:23:32 onward. Original editorial captions, reframing, '
        'quick cuts, speed changes and a labelled replay. No affiliation or endorsement implied.\n\n'
        '"Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com)\n'
        'Licensed under Creative Commons: By Attribution 4.0\n'
        'https://creativecommons.org/licenses/by/4.0/\n'
        'Track: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
        'Music excerpted, mixed and paused for comedic timing.\n\n'
        f'Private non-commercial review draft.\nProduction: {MARKER}\nSHA256: {m["sha256"]}'
    )
    state_path = folder/'youtube-upload-state.json'
    with pipeline_lock(cfg.data_dir):
        client = youtube.service(cfg)
        actual = youtube.channel_id(client)
        if actual != cfg.expected_channel_id or actual != 'UCnsktL-Bj_6QCKq5iW7bLaQ':
            raise ValueError('Wrong destination channel')
        if state_path.exists():
            state = json.loads(state_path.read_text())
            if state.get('sha256') != m['sha256']:
                raise ValueError('Upload state belongs to different media')
            if not state.get('video_id'):
                raise ValueError('Earlier upload outcome uncertain; reconcile before retrying')
        else:
            # Check every retained local upload record for this exact file.
            for p in cfg.data_dir.rglob('*upload*.json'):
                prior = json.loads(p.read_text(encoding='utf-8-sig'))
                if prior.get('sha256') == m['sha256']:
                    raise ValueError(f'Existing or uncertain upload record: {p}')
            # Check recent remote uploads for this stable production marker/hash.
            channel = client.channels().list(part='contentDetails',mine=True).execute()['items'][0]
            playlist = channel['contentDetails']['relatedPlaylists']['uploads']
            token = None
            for _ in range(2):
                page = client.playlistItems().list(part='contentDetails',playlistId=playlist,
                                                  maxResults=50,pageToken=token).execute()
                ids = [x['contentDetails']['videoId'] for x in page.get('items',[])]
                if ids:
                    videos = client.videos().list(part='snippet',id=','.join(ids)).execute()
                    for item in videos.get('items',[]):
                        desc = item['snippet'].get('description','')
                        if MARKER in desc or m['sha256'] in desc:
                            raise ValueError('Matching remote upload exists; reconcile instead of inserting')
                token = page.get('nextPageToken')
                if not token:
                    break
            save_json(folder/'private-upload-rights-review.json',{
                'checked':now(),'user_requested_upload':True,
                'permitted_action':'Private non-commercial review upload only',
                'rules_sha256':digest(rules),'source_scope':'Kai GTA VI marathon beginning November 18',
                'campaign_review':'Retained prior signed-in campaign review in evidence/prior-campaign-review.json',
                'editorial_review':'Kai facecam throughout; actual pickup ride and tumble; labelled replay; no invented dialogue or logos',
                'music':'Kevin MacLeod / Monkeys Spinning Monkeys, CC BY 4.0; retained official evidence and attribution',
                'public_release_cleared':False,'commercial_use_cleared':False,
                'remaining_review':'Original stream audio and Studio copyright restrictions before public release; no campaign submission',
                'duplicate_checks':'Local upload records and up to 100 recent remote uploads checked by marker/hash'})
            (folder/'youtube-description.txt').write_text(description,encoding='utf-8')
            m['upload_authorized'] = True
            m['upload_authorization'] = 'User: Upload this. Private review only; publishing remains disabled.'
            save_json(folder/'manifest.json',m)
            save_json(state_path,{'status':'uploading','started':now(),'sha256':m['sha256'],'channel_id':actual})
            request = client.videos().insert(part='snippet,status',body={
                'snippet':{'title':'Kai Cenat tries GTA VI budget transport #Shorts',
                           'description':description,'categoryId':'20',
                           'tags':['Kai Cenat','GTA VI','GTA 6','gaming','Shorts']},
                'status':{'privacyStatus':'private','selfDeclaredMadeForKids':False,'containsSyntheticMedia':False}},
                media_body=MediaFileUpload(str(output),mimetype='video/mp4',chunksize=8*1024*1024,resumable=True))
            response = None
            while response is None:
                progress,response = request.next_chunk(num_retries=3)
                if progress:
                    print(f'Upload {progress.progress():.0%}',flush=True)
            state = {'status':'uploaded','video_id':response['id'],
                     'url':'https://www.youtube.com/watch?v='+response['id'],
                     'privacy':'private','channel_id':actual,'time':now(),'sha256':m['sha256']}
            save_json(state_path,state)
            save_json(folder/'upload-result.json',state)
        # Verify the resulting remote video without exposing credentials or session URLs.
        remote = client.videos().list(part='snippet,status,processingDetails',id=state['video_id']).execute()['items'][0]
        if remote['snippet']['channelId'] != actual or remote['status']['privacyStatus'] != 'private':
            raise ValueError('Remote destination/privacy verification failed')
        verification = {'checked':now(),'video_id':remote['id'],'channel_id':actual,
                        'privacy':remote['status']['privacyStatus'],
                        'upload_status':remote['status'].get('uploadStatus'),
                        'processing':remote.get('processingDetails',{}).get('processingStatus')}
        save_json(folder/'upload-verification.json',verification)
        m.update(uploaded=True,upload_id=state['video_id'],upload_privacy='private')
        save_json(folder/'manifest.json',m)
        print(json.dumps({'upload':state,'verification':verification}),flush=True)

if __name__=='__main__':
    main()
