"""Explicit private review upload of the approved cut; no publishing or scheduling."""
import argparse
import json
from pathlib import Path
from googleapiclient.http import MediaFileUpload
from shorts.core import load, digest, save_json, now, pipeline_lock
from shorts import youtube, media

FOLDER = 'gta6-meme-test/permanent-record-draft-20261122-010739'
MARKER = 'manual-gta6-permanent-record-20261122-010739'
EXPECTED_HASH = '9a30f31718a49529e204c8d7dba6e968d84d2a1fad4e60022908abc55480c8b6'

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--upload',action='store_true',help='Perform the explicitly authorized private upload')
    args=parser.parse_args()
    cfg=load(Path(__file__).resolve().parents[1]/'config.json')
    assert not cfg.upload_enabled and not cfg.auto_publish, 'Keep daily uploads and publishing disabled'
    folder=cfg.data_dir/FOLDER
    m=json.loads((folder/'manifest.json').read_text(encoding='utf-8'))
    output=Path(m['output'])
    if digest(output)!=EXPECTED_HASH or m['sha256']!=EXPECTED_HASH or digest(Path(m['source']))!=m['source_sha256']:
        raise ValueError('Approved media changed')
    music=cfg.data_dir/'fortnite-test/music/Monkeys Spinning Monkeys.mp3'
    assert digest(music)==m['music_sha256'], 'Licensed music changed'
    rules=folder/'evidence/rules.txt'
    assert all(x in rules.read_text(encoding='utf-8-sig') for x in ['Kai Cenat','YouTube Shorts','#KAICENAT'])
    qc=m['technical_qc']
    assert qc['decode']=='passed' and not qc['black_events'] and not qc['silence_events'] and float(qc['peak_db'][0])<-.1
    info=media.probe(cfg,output)
    video=next(s for s in info['streams'] if s['codec_type']=='video')
    assert (video['width'],video['height'])==(1080,1920) and 19.5<float(info['format']['duration'])<19.8
    description=(
        'Kai Cenat discovers GTA VI keeps a permanent record. NOW you tell me. #KAICENAT #GTA6 #Shorts\n\n'
        'Gameplay and reactions: Kai Cenat (@KaiCenat). Fan-made editorial meme edit.\n'
        'Creator: https://www.twitch.tv/kaicenat\n'
        'Recording: https://archive.org/details/kai-cenats-first-time-playing-gta-6\n'
        'Selected action: 01:22:48.5 to 01:23:28.1. Original captions, quick cuts, reframing, '
        'speed change and enlarged actual game warning. Captions are editorial jokes, not quotations. '
        'No affiliation or endorsement implied.\n\n'
        '"Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com)\n'
        'Licensed under Creative Commons: By Attribution 4.0\n'
        'https://creativecommons.org/licenses/by/4.0/\n'
        'Track: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
        'Music excerpted and mixed with a timed pause.\n\n'
        'Private non-commercial review draft. Official-source provenance and public/commercial '
        'release clearance remain under review. No campaign submission.\n'
        f'Production: {MARKER}\nSHA256: {EXPECTED_HASH}'
    )
    state_path=folder/'youtube-upload-state.json'
    with pipeline_lock(cfg.data_dir):
        client=youtube.service(cfg)
        actual=youtube.channel_id(client)
        assert actual==cfg.expected_channel_id=='UCnsktL-Bj_6QCKq5iW7bLaQ', 'Wrong destination channel'
        # Retain official-channel metadata if available without claiming a frame match.
        official=client.videos().list(part='snippet,contentDetails',id='7mjhCbFi9Wk').execute()
        save_json(folder/'evidence/official-video-metadata.json',{'checked':now(),'response':official,
            'review':'Candidate official source only; exact correspondence to archived recording remains unverified.'})
        state=None
        if state_path.exists():
            state=json.loads(state_path.read_text())
            assert state.get('sha256')==EXPECTED_HASH, 'Upload state is for different media'
            if not state.get('video_id'):
                raise ValueError('Previous upload outcome uncertain; reconcile before any new insert')
        else:
            for p in cfg.data_dir.rglob('*upload*.json'):
                prior=json.loads(p.read_text(encoding='utf-8-sig'))
                if prior.get('sha256')==EXPECTED_HASH:
                    raise ValueError(f'Existing or uncertain upload record: {p.name}; reconcile first')
            channel=client.channels().list(part='contentDetails',mine=True).execute()['items'][0]
            playlist=channel['contentDetails']['relatedPlaylists']['uploads']
            token=None
            for _ in range(2):
                page=client.playlistItems().list(part='contentDetails',playlistId=playlist,maxResults=50,pageToken=token).execute()
                ids=[x['contentDetails']['videoId'] for x in page.get('items',[])]
                if ids:
                    for item in client.videos().list(part='snippet',id=','.join(ids)).execute().get('items',[]):
                        desc=item['snippet'].get('description','')
                        if MARKER in desc or EXPECTED_HASH in desc:
                            raise ValueError('Matching remote upload already exists; reconcile instead of inserting')
                token=page.get('nextPageToken')
                if not token: break
            print(json.dumps({'preflight':'passed','channel_id':actual,'privacy':'private','sha256':EXPECTED_HASH,
                'bytes':output.stat().st_size,'duplicate_check':'local records and recent remote uploads checked'}),flush=True)
            if not args.upload: return
            save_json(folder/'private-upload-rights-review.json',{
                'checked':now(),'user_requested_upload':True,'user_instruction':'Upload it',
                'authorized_action':'Private non-commercial review upload of the preview only',
                'campaign_rules_sha256':digest(rules),'campaign_membership_evidence':'evidence/prior-campaign-membership.json',
                'source_review':'Kai marathon footage; existing archive excerpt; distinct source ranges from uploaded garage/pickup edits.',
                'remaining_review':'Official-channel provenance, stream audio listening, publisher policy and public/commercial release remain unestablished. User requested upload after disclosure; no campaign submission or public release.',
                'editorial_review':'Kai camera throughout; actual criminal-profile warning; original game-focused captions; no narration or added footer.',
                'music':'Monkeys Spinning Monkeys / Kevin MacLeod, CC BY 4.0; licence evidence and attribution retained.',
                'public_release_cleared':False,'commercial_use_cleared':False})
            (folder/'youtube-description.txt').write_text(description,encoding='utf-8')
            m.update(upload_authorized=True,upload_authorization='User: Upload it. Private review only; publishing stays disabled.')
            save_json(folder/'manifest.json',m)
            # Durable reservation precedes the remote insert; uncertain outcomes block retries.
            save_json(state_path,{'status':'uploading','started':now(),'sha256':EXPECTED_HASH,'channel_id':actual})
            request=client.videos().insert(part='snippet,status',body={
                'snippet':{'title':'Kai Cenat discovers GTA VI keeps a permanent record #Shorts','description':description,
                    'categoryId':'20','tags':['Kai Cenat','GTA VI','GTA 6','gaming','Shorts']},
                'status':{'privacyStatus':'private','selfDeclaredMadeForKids':False,'containsSyntheticMedia':False}},
                media_body=MediaFileUpload(str(output),mimetype='video/mp4',chunksize=8*1024*1024,resumable=True))
            response=None
            while response is None:
                progress,response=request.next_chunk(num_retries=3)
                if progress: print(f'Upload {progress.progress():.0%}',flush=True)
            state={'status':'uploaded','video_id':response['id'],'url':'https://www.youtube.com/watch?v='+response['id'],
                'privacy':'private','channel_id':actual,'time':now(),'sha256':EXPECTED_HASH}
            save_json(state_path,state)
            save_json(folder/'upload-result.json',state)
        remote=client.videos().list(part='snippet,status,processingDetails',id=state['video_id']).execute()['items'][0]
        assert remote['snippet']['channelId']==actual and remote['status']['privacyStatus']=='private', 'Remote destination/privacy mismatch'
        verification={'checked':now(),'video_id':remote['id'],'channel_id':actual,'privacy':remote['status']['privacyStatus'],
            'upload_status':remote['status'].get('uploadStatus'),'processing':remote.get('processingDetails',{}).get('processingStatus')}
        save_json(folder/'upload-verification.json',verification)
        m.update(uploaded=True,upload_id=state['video_id'],upload_privacy='private')
        save_json(folder/'manifest.json',m)
        print(json.dumps({'upload':state,'verification':verification}),flush=True)

if __name__=='__main__':
    main()
