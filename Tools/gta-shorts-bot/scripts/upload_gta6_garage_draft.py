"""One explicitly requested PRIVATE review upload. Never publishes or submits a campaign.

Uses separate durable upload state because this 28s non-commercial review draft
does not meet the commercial-source requirements of the automated daily queue.
No daily permissions, production QC limits, or automation settings are changed.
"""
import json
from pathlib import Path

from googleapiclient.http import MediaFileUpload

from shorts.core import load, digest, save_json, now, pipeline_lock
from shorts import media, youtube


def main():
    cfg = load(Path('config.json'))
    folder = cfg.data_dir / 'gta6-meme-test/garage-draft-20261119-191606'
    manifest = json.loads((folder / 'manifest.json').read_text(encoding='utf-8'))
    output = Path(manifest['output'])
    if digest(output) != manifest['sha256']:
        raise ValueError('Reviewed video has changed')
    rules_path = cfg.data_dir / 'gta6-meme-test/kai-full-campaign-rules.txt'
    rules = rules_path.read_text(encoding='utf-8-sig')
    if 'YouTube Shorts' not in rules or '#KaiCenat' not in rules:
        raise ValueError('Missing reviewed campaign rules')
    qc = json.loads((folder / 'audio-picture-qc.json').read_text(encoding='utf-8'))
    if qc['decode_exit'] != 0 or qc['extended_silence'] or qc['black_frames'] or float(qc['peak_db']) > -.1:
        raise ValueError('Draft technical QC failed')
    info = media.probe(cfg, output)
    v = next(s for s in info['streams'] if s['codec_type'] == 'video')
    if (v['width'], v['height']) != (1080, 1920) or not 27 <= float(info['format']['duration']) <= 29:
        raise ValueError('Unexpected video format or duration')
    review = {
        'checked': now(), 'user_requested_upload': True,
        'permitted_action': 'Private, non-commercial YouTube review upload only',
        'campaign_joined': 'Observed Joined in signed-in campaign UI',
        'campaign': 'https://contentrewards.com/c/campaigns/1d2bcb00-c904-415f-aa82-f95b96baad86',
        'rules_url': 'https://docs.google.com/document/d/1z3h2gDIdr2LFgDrSkXf6rIIA72y62nluKHdWgki5iG0/edit',
        'rules_sha256': digest(rules_path),
        'source_scope': 'Campaign allows any stream, video or clip featuring Kai during his GTA VI marathon beginning November 18',
        'source': 'https://archive.org/details/kai-cenats-first-time-playing-gta-6',
        'editorial_review': 'Kai facecam throughout; actual garage events; playful game-focused captions, no invented dialogue or negative personal claims; no added logos or watermarks',
        'credit': 'Kai Cenat in title, source/creator credit and #KaiCenat in description',
        'music': 'Monkeys Spinning Monkeys / Kevin MacLeod; retained official CC BY 4.0 evidence and attribution',
        'publisher_policy': 'https://support.rockstargames.com/articles/7bNaeoMFTV0iUDGhStTXvz/policy-on-posting-copyrighted-rockstar-games-material',
        'commercial_use_cleared': False,
        'public_release_cleared': False,
        'remaining_review': 'Original stream audio may contain game music. Review Studio copyright restrictions before any public release. No campaign submission or revenue claim is authorized by this script.',
    }
    title = "Kai Cenat discovers GTA VI's real final boss: the garage #Shorts"
    description = (
        'GTA VI launch day: one more car upgrade. #KaiCenat #GTA6 #Shorts\n\n'
        'Gameplay and reactions: Kai Cenat (@KaiCenat). Fan-made editorial meme edit.\n'
        'Creator: https://www.twitch.tv/kaicenat\n'
        'Source recording: Kai Cenat\'s First Time Playing GTA 6!\n'
        'https://archive.org/details/kai-cenats-first-time-playing-gta-6\n'
        'Selected from the garage/customisation section beginning at 01:18:00. '
        'Changes: selected excerpts, reframing, speed changes, original meme captions and added background music. '
        'No affiliation or endorsement implied.\n\n'
        '"Monkeys Spinning Monkeys" Kevin MacLeod (incompetech.com)\n'
        'Licensed under Creative Commons: By Attribution 4.0\n'
        'https://creativecommons.org/licenses/by/4.0/\n'
        'Track source: https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011\n'
        'Music excerpted, mixed under speech and stopped for comedic timing.\n\n'
        'Private non-commercial review draft.\n'
        'Production: manual-gta6-garage-20261119-191606'
    )
    state_path = folder / 'youtube-upload-state.json'
    with pipeline_lock(cfg.data_dir):
        if state_path.exists():
            state = json.loads(state_path.read_text(encoding='utf-8'))
            if state.get('video_id'):
                print(json.dumps(state), flush=True)
                return
            raise ValueError('An earlier upload may exist; reconcile in Studio before retrying')
        client = youtube.service(cfg)
        actual_channel = youtube.channel_id(client)
        if actual_channel != cfg.expected_channel_id or actual_channel != 'UCnsktL-Bj_6QCKq5iW7bLaQ':
            raise ValueError('Wrong YouTube destination')
        save_json(folder / 'private-upload-rights-review.json', review)
        (folder / 'youtube-description.txt').write_text(description, encoding='utf-8')
        save_json(state_path, {'status': 'uploading', 'started': now(),
                              'sha256': manifest['sha256'], 'channel_id': actual_channel})
        request = client.videos().insert(part='snippet,status', body={
            'snippet': {'title': title, 'description': description, 'categoryId': '20',
                        'tags': ['GTA VI', 'GTA 6', 'Kai Cenat', 'gaming', 'Shorts']},
            'status': {'privacyStatus': 'private', 'selfDeclaredMadeForKids': False,
                       'containsSyntheticMedia': False}},
            media_body=MediaFileUpload(str(output), mimetype='video/mp4',
                                       chunksize=8*1024*1024, resumable=True))
        response = None
        while response is None:
            progress, response = request.next_chunk(num_retries=3)
            if progress:
                print(f'Upload {progress.progress():.0%}', flush=True)
        result = {'status': 'uploaded', 'video_id': response['id'],
                  'url': 'https://www.youtube.com/watch?v='+response['id'],
                  'privacy': 'private', 'channel_id': actual_channel, 'time': now(),
                  'sha256': manifest['sha256']}
        save_json(state_path, result)
        save_json(folder / 'upload-result.json', result)
        print(json.dumps(result), flush=True)


if __name__ == '__main__':
    main()
