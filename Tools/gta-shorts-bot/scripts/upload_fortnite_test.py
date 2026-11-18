"""Idempotent, explicitly requested private upload of the reviewed Fortnite cut."""
from pathlib import Path
import json

from shorts.core import load, Store, save_json, digest, now, pipeline_lock
from shorts.rights import Rights, import_file
from shorts import media, youtube


def main():
    base = load(Path('config.json'))
    # Do not enable the scheduled uploader or public publishing.
    cfg = base.model_copy(update={'upload_enabled': True, 'auto_publish': False})
    store = Store(cfg)
    root = cfg.data_dir / 'fortnite-test'
    folder = root / 'meme-clean-20261118-204211'
    edit = json.loads((folder / 'manifest.json').read_text())
    output = Path(edit['output'])
    if digest(output) != edit['sha256']:
        raise RuntimeError('Video changed since review; refusing to upload')
    metadata = json.loads((root / 'noc-metadata.json').read_text())
    if metadata['metadata'].get('licenseurl') != 'https://creativecommons.org/licenses/by/4.0/':
        raise RuntimeError('Footage licence record requires review')
    if not (root / 'music/licence-source.html').is_file() or not (root / 'music/Monkeys Spinning Monkeys.mp3').is_file():
        raise RuntimeError('Music and licence evidence are required')
    review = {
        'checked': now(),
        'footage': {'creator': 'Noc / Noc VODs',
                    'source': 'https://archive.org/details/12-2-23-p-2-fn-customs',
                    'license': 'https://creativecommons.org/licenses/by/4.0/',
                    'evidence': str(root / 'noc-metadata.json'),
                    'source_audio': 'Excluded from acquired derivative'},
        'music': {'title': 'Monkeys Spinning Monkeys', 'creator': 'Kevin MacLeod (incompetech.com)',
                  'source': 'https://incompetech.com/music/royalty-free/index.html?isrc=USUAN1400011',
                  'license': 'https://creativecommons.org/licenses/by/4.0/',
                  'evidence': str(root / 'music/licence-source.html'),
                  'sha256': digest(root / 'music/Monkeys Spinning Monkeys.mp3')},
        'publisher': {'policy': 'https://legal.epicgames.com/epicgames/fan-art-policy',
                      'scope': 'Individual fan web video with advertising exception; no Epic endorsement; disclaimer included'},
        'credits': 'Creator credits, source/license links, changes and publisher disclaimer in upload description',
        'visibility': 'private', 'other_audio': 'Original synthesized effects; no source streamer audio',
    }
    save_json(folder / 'upload-rights-review.json', review)
    source_id = 'archive:12-2-23-p-2-fn-customs:384-599:silent'
    production_id = 'manual-fortnite-roomba-20261118-204211'
    with pipeline_lock(cfg.data_dir):
        existing = store.db.execute('SELECT * FROM productions WHERE id=?', (production_id,)).fetchone()
        if existing is None:
            rights = Rights(
                source_id=source_id, creator='Noc / Noc VODs', source_url=review['footage']['source'],
                basis='licensed', evidence=str(folder / 'upload-rights-review.json'), commercial_use=True,
                editing=True, youtube_distribution=True, third_party_audio_cleared=True,
                game_content_cleared=True, access_authorized=True,
                attribution='Gameplay: Noc Videos copyright 2018-2026 Noc; CC BY 4.0. Edited/reframed; source audio removed. Credits/disclaimer in description.')
            source = root / 'noc-game-one-silent.mp4'
            import_file(cfg, store, source, rights)
            clips = []
            for start, length in [(44, 3.125), (105.9, 1.875), (136.8, .08)]:
                clips.append({'source_id': source_id, 'moment': {'start': start, 'end': start+length,
                              'fingerprint': media.fingerprint(cfg, source, start, length, folder)}})
            manifest = {
                'id': production_id, 'source_ids': [source_id], 'clips': clips,
                'output': str(output), 'output_sha256': edit['sha256'], 'qc': media.quality_check(cfg, output),
                'script': {'title': edit['title']+' #Shorts', 'description': edit['description']},
                'credits': [], 'tags': ['Fortnite', 'Fortnite memes', 'gaming', 'Shorts'],
                'production_note': 'Original caption-led meme edit. #Fortnite #Shorts',
                'rights_review': str(folder / 'upload-rights-review.json'),
                'origin': 'User-requested one-off private upload; daily upload configuration unchanged',
            }
            with store.db:
                store.db.execute("INSERT INTO productions VALUES(?,?,'rendered',?,NULL,NULL,?)",
                                 (production_id, 'manual:fortnite:20261118:204211', json.dumps(manifest), now()))
                store.db.execute('UPDATE sources SET used_by=? WHERE id=?', (production_id, source_id))
        print('Uploading reviewed clean cut privately...', flush=True)
        video_id = youtube.upload(cfg, store, production_id)
        result = {'video_id': video_id, 'url': 'https://www.youtube.com/watch?v='+video_id,
                  'privacy': 'private', 'channel_id': cfg.expected_channel_id,
                  'production_id': production_id, 'time': now()}
        save_json(folder / 'upload-result.json', result)
        print(json.dumps(result, indent=2), flush=True)
    store.db.close()


if __name__ == '__main__':
    main()
