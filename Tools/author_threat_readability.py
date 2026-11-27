"""Explicit, one-time authoring of threat cues. Never runs from Unity import/reload.

Existing authored cues are skipped so Inspector customisations survive subsequent runs.
"""
from pathlib import Path
import math
import random
import re
import struct
import uuid
import wave

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'Assets/_Project'


def guid(path):
    return re.search(r'guid: (\w+)', Path(str(path) + '.meta').read_text()).group(1)


def meta(path, template=None):
    target = Path(str(path) + '.meta')
    if not target.exists():
        value = uuid.uuid4().hex
        text = re.sub(r'guid: \w+', 'guid: ' + value, template) if template else f'fileFormatVersion: 2\nguid: {value}\n'
        target.write_text(text)
    return guid(path)


def audio(kind, phase, duration):
    path = ASSETS / f'Audio/Threats/{kind}_{phase}.wav'
    if path.exists():
        return guid(path)
    rate = 24000
    rng = random.Random(kind + phase)
    samples = []
    oscillator = 0.0
    for i in range(int(rate * duration)):
        t = i / rate
        u = t / duration
        noise = rng.uniform(-1, 1)
        envelope = min(1, t / .012, (duration - t) / .045)
        if kind == 'Grappler':
            frequency = 220 + 550 * (u if phase == 'Prepare' else 1 - u)
            oscillator += frequency / rate
            pulse = math.exp(-((t * (11 + 9 * u)) % 1) * 12)
            value = (.55 * math.sin(oscillator * math.tau) + noise * .3) * (.25 + .75 * pulse)
        elif kind == 'Rocket':
            frequency = 90 + 250 * u
            oscillator += frequency / rate
            value = noise * (.3 + .4 * u) + .2 * math.sin(oscillator * math.tau)
            value *= .3 + .7 * u if phase == 'Prepare' else (1 - u)
            if phase == 'Flight':
                value = .3 * math.sin(t * math.tau * 650) + .12 * math.sin(t * math.tau * 975) + noise * .1
        else:
            oscillator += (112 + (u * 45 if phase == 'Prepare' else 0)) / rate
            horn = sum(math.sin(oscillator * math.tau * h) / h for h in (1, 2, 3, 4)) * .35
            double = 1 if t < .18 or .30 < t < .50 else .15
            value = horn * double + .12 * noise * u
        if phase == 'Commit':
            value *= (1 - u)
        if phase == 'Recover':
            value *= .5 * (1 - u)
        samples.append(int(max(-.95, min(.95, value * envelope * .65)) * 32767))
    with wave.open(str(path), 'wb') as output:
        output.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
        output.writeframes(struct.pack('<' + 'h' * len(samples), *samples))
    template = (ASSETS / 'Audio/Car/truck_horn.wav.meta').read_text()
    template = template.replace('normalize: 1', 'normalize: 0')
    return meta(path, template)


def blocks(text):
    return re.findall(r'--- !u!\d+ &-?\d+[^\n]*\n.*?(?=--- !u!|\Z)', text, re.S)


def vector(v):
    return '{' + ', '.join(f'{axis}: {n:.7g}' for axis, n in zip('xyz', v)) + '}'


def author(prefab, kind, y):
    path = ASSETS / ('Prefabs/Enemies/' + prefab + '.prefab')
    original = path.read_text(encoding='utf-8-sig')
    if 'm_preparationSignal:' in original:
        print('Preserved existing cue:', prefab)
        return
    records = blocks(original)
    root_transform = next(b for b in records if b.startswith('--- !u!4 ') and '  m_Father: {fileID: 0}\n' in b)
    root_id = int(re.search(r'm_GameObject: \{fileID: (\d+)', root_transform).group(1))
    root_transform_id = int(re.search(r'&(-?\d+)', root_transform).group(1))
    root_object = next(b for b in records if b.startswith(f'--- !u!1 &{root_id}\n'))
    net_object = next(b for b in records if '  NetworkBehaviours:\n' in b)
    net_id = int(re.search(r'&(-?\d+)', net_object).group(1))
    net_list = re.search(r'  NetworkBehaviours:\n((?:  - .*\n)*)', net_object)
    index = net_list.group(1).count('fileID:')
    next_id = 710000000000000000
    while str(next_id) in original:
        next_id += 1000
    generated = []

    def allocate():
        nonlocal next_id
        next_id += 1
        return next_id

    depot = blocks((ASSETS / 'Prefabs/World/depot_02.prefab').read_text())
    renderer_template = next(b for b in depot if b.startswith('--- !u!23 '))
    audio_template = next(b for b in depot if b.startswith('--- !u!82 '))

    def node(name, parent, pos=(0, 0, 0), scale=(1, 1, 1), angle=0, material=None, active=True, cylinder=False, voice=False, yaw=0):
        go, tr = allocate(), allocate()
        extra = [allocate(), allocate()] if material else [allocate()] if voice else []
        base = f'  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {{fileID: 0}}\n  m_PrefabInstance: {{fileID: 0}}\n  m_PrefabAsset: {{fileID: 0}}\n'
        generated.append(f'--- !u!1 &{go}\nGameObject:\n' + base + '  serializedVersion: 6\n  m_Component:\n' +
            ''.join(f'  - component: {{fileID: {c}}}\n' for c in [tr] + extra) +
            f'  m_Layer: 0\n  m_Name: {name}\n  m_TagString: Untagged\n  m_Icon: {{fileID: 0}}\n  m_NavMeshLayer: 0\n  m_StaticEditorFlags: 0\n  m_IsActive: {int(active)}\n')
        rad = math.radians(angle) / 2
        yaw_rad = math.radians(yaw) / 2
        generated.append(f'--- !u!4 &{tr}\nTransform:\n' + base + f'  m_GameObject: {{fileID: {go}}}\n  serializedVersion: 2\n' +
            f'  m_LocalRotation: {{x: {math.sin(yaw_rad)*math.sin(rad):.7g}, y: {math.sin(yaw_rad)*math.cos(rad):.7g}, z: {math.cos(yaw_rad)*math.sin(rad):.7g}, w: {math.cos(yaw_rad)*math.cos(rad):.7g}}}\n' +
            f'  m_LocalPosition: {vector(pos)}\n  m_LocalScale: {vector(scale)}\n  m_ConstrainProportionsScale: 0\n  m_Children: []\n  m_Father: {{fileID: {parent}}}\n  m_LocalEulerAnglesHint: {{x: 0, y: 0, z: {angle}}}\n')
        if material:
            generated.append(f'--- !u!33 &{extra[0]}\nMeshFilter:\n' + base + f'  m_GameObject: {{fileID: {go}}}\n  m_Mesh: {{fileID: {10206 if cylinder else 10202}, guid: 0000000000000000e000000000000000, type: 0}}\n')
            mesh = re.sub(r'&\d+\n', f'&{extra[1]}\n', renderer_template, count=1)
            mesh = re.sub(r'm_GameObject: \{fileID: \d+}', f'm_GameObject: {{fileID: {go}}}', mesh)
            mesh = re.sub(r'guid: [a-f0-9]{32}', f'guid: {material}', mesh)
            mesh = mesh.replace('m_CastShadows: 1', 'm_CastShadows: 0')
            mesh = mesh.replace('m_PreserveUVs: 0', 'm_PreserveUVs: 1')
            generated.append(mesh)
        if voice:
            source = re.sub(r'&\d+\n', f'&{extra[0]}\n', audio_template, count=1)
            source = re.sub(r'm_GameObject: \{fileID: \d+}', f'm_GameObject: {{fileID: {go}}}', source)
            source = re.sub(r'm_Resource: .*', 'm_Resource: {fileID: 0}', source)
            for field, value in [('m_Volume', '.8'), ('Loop', '0'), ('Priority', '50'), ('MinDistance', '4'), ('MaxDistance', '38')]:
                source = re.sub(rf'  {field}: .*', f'  {field}: {value}', source)
            source = re.sub(r'(  panLevelCustomCurve:.*?      value:) 0', r'\1 1', source, flags=re.S)
            generated.append(source)
        return tr, extra[0] if voice else go

    material = guid(ASSETS / 'Prefabs/Weapons/RocketLauncher/Materials/RL_Yellow.mat')
    recovery_mat = guid(ASSETS / 'Prefabs/Weapons/RocketLauncher/Materials/RL_Blue.mat')
    component = allocate()
    arrival_tr, arrival_source = node('Threat Arrival Voice', root_transform_id, voice=True)
    attack_tr, attack_source = node('Threat Attack Voice', root_transform_id, voice=True)
    signal, _ = node('Preparation Signal', root_transform_id, (0, y, 0), active=False)
    recovery, _ = node('Recovery Signal', root_transform_id, (0, y, 0), active=False)
    direction, _ = node('Attack Bearing', root_transform_id, (0, .08, 0), active=False)
    # Short physical arrow: occluded by scenery, not an x-ray beam or an impact-radius claim.
    node('Bearing Stem', direction, (0, 0, .45), (.055, .035, .9), material=material)
    node('Bearing Head Left', direction, (-.12, 0, .82), (.32, .035, .08), material=material, yaw=-45)
    node('Bearing Head Right', direction, (.12, 0, .82), (.32, .035, .08), material=material, yaw=45)
    identification = 0
    # Different silhouettes as well as sound; these do not rely on colour recognition.
    if kind == 'Grappler':
        for name, pos, scale in [('Hook Top', (0, .2, 0), (.5, .09, .09)), ('Hook Side', (-.2, 0, 0), (.09, .45, .09)), ('Hook Foot', (0, -.2, 0), (.5, .09, .09)), ('Hook Tip', (.2, -.1, 0), (.09, .2, .09))]:
            node(name, signal, pos, scale, material=material)
        # Permanent back-mounted reel makes this archetype identifiable before winding up.
        mount, _ = node('Grappler Reel Silhouette', root_transform_id, (0, 1.15, -.3))
        identification = mount
        node('Reel Top', mount, (0, .17, 0), (.48, .08, .18), material=material)
        node('Reel Bottom', mount, (0, -.17, 0), (.48, .08, .18), material=material)
        node('Reel Left', mount, (-.2, 0, 0), (.08, .4, .18), material=material)
        node('Reel Right', mount, (.2, 0, 0), (.08, .4, .18), material=material)
    elif kind == 'Rocket':
        node('Ignition Stem', signal, (0, .06, 0), (.14, .4, .14), material=material)
        node('Ignition Dot', signal, (0, -.25, 0), (.14, .12, .14), material=material)
        node('Ignition Fin Left', signal, (-.2, -.05, 0), (.09, .3, .09), -25, material)
        node('Ignition Fin Right', signal, (.2, -.05, 0), (.09, .3, .09), 25, material)
    else:
        for x, angle in [(-.15, -40), (.15, 40)]:
            node('Charge Chevron', signal, (x, 0, 0), (.1, .5, .12), angle, material)
        roof, _ = node('Rogue Roof Identifier', root_transform_id, (0, 2.7, 0))
        identification = roof
        node('Roof Stripe Left', roof, (-.25, 0, 0), (.12, .12, .65), material=material)
        node('Roof Stripe Right', roof, (.25, 0, 0), (.12, .12, .65), material=material)
    for x in (-.15, .15):
        node('Recovery Bar', recovery, (x, 0, 0), (.1, .4, .1), material=recovery_mat)
    # Fill child links for every authored transform.
    all_children = {}
    for record in generated:
        if record.startswith('--- !u!4 '):
            child = int(re.search(r'&(\d+)', record).group(1))
            parent = int(re.search(r'm_Father: \{fileID: (\d+)', record).group(1))
            all_children.setdefault(parent, []).append(child)
    for i, record in enumerate(generated):
        if record.startswith('--- !u!4 '):
            own = int(re.search(r'&(\d+)', record).group(1))
            if own in all_children:
                generated[i] = record.replace('  m_Children: []\n', '  m_Children:\n' + ''.join(f'  - {{fileID: {c}}}\n' for c in all_children[own]))
    root_transform_new = re.sub(r'(  m_Children:\n(?:  - .*\n)*)', lambda m: m.group(1) + ''.join(f'  - {{fileID: {c}}}\n' for c in all_children[root_transform_id]), root_transform, count=1)
    original = original.replace(root_transform, root_transform_new)
    original = original.replace(root_object, root_object.replace('  m_Layer:', f'  - component: {{fileID: {component}}}\n  m_Layer:', 1))
    original = original.replace(net_list.group(0), net_list.group(0) + f'  - {{fileID: {component}}}\n', 1)
    # Bind the existing attack controller without replacing any existing references.
    controller_guid = guid(ASSETS / ('Scripts/Gameplay/Enemies/' + {'Grappler': 'GrapplerThief', 'Rocket': 'EnemyWeapon', 'RogueCar': 'RogueCar'}[kind] + '.cs'))
    for record in blocks(original):
        if f'guid: {controller_guid}' in record:
            original = original.replace(record, record + f'  m_telegraph: {{fileID: {component}}}\n')
            break
    clips = {phase: audio(kind, phase, length) for phase, length in [('Arrival', .6), ('Prepare', {'Grappler': 1.1, 'Rocket': 1.5, 'RogueCar': 1.8}[kind]), ('Commit', .25), ('Recover', 1.2)]}
    generated.append(f'''--- !u!114 &{component}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {root_id}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {guid(ASSETS / 'Scripts/Gameplay/Enemies/ThreatTelegraph.cs')}, type: 3}}
  m_Name:
  m_EditorClassIdentifier: Assembly-CSharp::CouchGuys.Gameplay.Enemies.ThreatTelegraph
  _componentIndexCache: {index}
  _addedNetworkObject: {{fileID: {net_id}}}
  _networkObjectCache: {{fileID: {net_id}}}
  m_arrivalSource: {{fileID: {arrival_source}}}
  m_attackSource: {{fileID: {attack_source}}}
  m_arrivalClip: {{fileID: 8300000, guid: {clips['Arrival']}, type: 3}}
  m_preparationClip: {{fileID: 8300000, guid: {clips['Prepare']}, type: 3}}
  m_commitClip: {{fileID: 8300000, guid: {clips['Commit']}, type: 3}}
  m_recoveryClip: {{fileID: 8300000, guid: {clips['Recover']}, type: 3}}
  m_preparationSignal: {{fileID: {signal}}}
  m_directionMarker: {{fileID: {direction}}}
  m_recoverySignal: {{fileID: {recovery}}}
  m_identificationVisual: {{fileID: {identification}}}
  m_pulseFrequency: 4
  m_pulseScale: 0.18
  m_directionLength: {4 if kind == 'RogueCar' else 2}
  m_arrivalAudibleDistance: 35
''')
    path.write_text(original.rstrip() + '\n' + ''.join(generated), encoding='utf-8')
    print('Authored:', prefab)


def main():
    folder = ASSETS / 'Audio/Threats'
    folder.mkdir(exist_ok=True)
    meta(folder)
    meta(ASSETS / 'Scripts/Gameplay/Enemies/ThreatTelegraph.cs')
    author('GrapplerThief', 'Grappler', 2.05)
    author('Thief_RocketLauncher', 'Rocket', 2.05)
    author('RogueCar', 'RogueCar', 3.4)
    author_rocket_flight()
    settings = ASSETS / 'Settings/Thieves/GrapplerThiefSettings.asset'
    text = settings.read_text()
    if 'AimCommitLead:' not in text:
        settings.write_text(text.replace('  AimWindup: 0.6', '  AimWindup: 1.1\n  AimCommitLead: 0.35\n  RewindDuration: 1.2'))
    settings = ASSETS / 'Settings/Thieves/RocketLauncherThiefSettings.asset'
    text = settings.read_text()
    if 'RocketWindup:' not in text:
        settings.write_text(text.replace('  Combat:\n', '  Combat:\n    RocketWindup: 1.5\n    RocketAimCommitLead: 0.45\n    RocketRecovery: 1.5\n'))
    for settings in (ASSETS / 'Settings').rglob('RogueCarSettings*.asset'):
        text = settings.read_text()
        if 'ChargeWindup:' not in text:
            settings.write_text(text + '  ChargeRange: 24\n  ChargeWindup: 1.8\n  ChargeAimCommitLead: 0.6\n  ChargeDuration: 2\n  ChargeRecovery: 2.2\n  ApproachSpeedMultiplier: 0.4\n  ChargeTurnSpeed: 12\n')


def author_rocket_flight():
    path = ASSETS / 'Prefabs/Weapons/RocketLauncher/RocketProjectile.prefab'
    text = path.read_text()
    if 'm_flightAudio:' in text:
        return
    clip = audio('Rocket', 'Flight', .8)
    records = blocks(text)
    root = next(b for b in records if '  m_Name: RocketProjectile\n' in b)
    root_id = re.search(r'&(\d+)', root).group(1)
    source_id = 711000000000000001
    while str(source_id) in text:
        source_id += 1
    text = text.replace(root, root.replace('  m_Layer:', f'  - component: {{fileID: {source_id}}}\n  m_Layer:', 1))
    for record in records:
        if '  m_explosionPrefab:' in record:
            text = text.replace(record, record + f'  m_flightAudio: {{fileID: {source_id}}}\n')
            break
    source = next(b for b in blocks((ASSETS / 'Prefabs/World/depot_02.prefab').read_text()) if b.startswith('--- !u!82 '))
    source = re.sub(r'&\d+\n', f'&{source_id}\n', source, count=1)
    source = re.sub(r'm_GameObject: \{fileID: \d+}', f'm_GameObject: {{fileID: {root_id}}}', source)
    source = re.sub(r'm_Resource: .*', f'm_Resource: {{fileID: 8300000, guid: {clip}, type: 3}}', source)
    for field, value in [('m_Volume', '.45'), ('Priority', '65'), ('MinDistance', '2'), ('MaxDistance', '26')]:
        source = re.sub(rf'  {field}: .*', f'  {field}: {value}', source)
    source = re.sub(r'(  panLevelCustomCurve:.*?      value:) 0', r'\1 1', source, flags=re.S)
    path.write_text(text + source)


if __name__ == '__main__':
    main()
