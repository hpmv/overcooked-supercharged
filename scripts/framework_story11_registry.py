"""Bounded, observation-only dynamic metadata reconciliation for the first dish.

No sockets: the runner invokes the existing paused RegistryObserver module.
Current absence receipts never stand in for a historical native removal event.
"""
import copy
import hashlib
import json

from framework_pause_boundary import native_physics, native_clock_fields
from framework_story11_planner import require, validate_observed_layout


def epoch(receipt):
    b = receipt['bridge']
    key = (b.get('session', {}).get('scene'), b.get('readyUnityFrame'),
           b.get('sceneMetadataRefreshes'), b.get('inputExchange', {}).get('connectionEpoch'))
    require(isinstance(key[0], str) and all(type(x) is int and x > 0 for x in key[1:]),
            'Missing native scene/connection observation epoch')
    return key


def world_proof(state, receipt):
    """Only telemetry publication may differ; native world and clocks may not."""
    return {'frame': state['frame'], 'epoch': epoch(receipt),
            'round': receipt['bridge']['nativeRound'], 'clocks': native_clock_fields(receipt),
            'food': {'source': receipt['detail']['source'], 'entities': receipt['detail']['entities']},
            'physics': native_physics(receipt)}


def result_of(response):
    result = response.get('detail', {}).get('result')
    require(isinstance(result, dict) and result.get('ok') is True,
            'Registry observation operation did not return a successful receipt')
    require(result.get('nativeStateChanged') is False and result.get('initialSetsChanged') is False,
            'Registry observation did not prove native world/initial sets unchanged')
    return result


class RegistryEvidence:
    def __init__(self):
        self.key = None
        self.proxies = {}

    def align(self, receipt):
        key = epoch(receipt)
        if key != self.key:
            self.key = key
            self.proxies = {}

    def rebranch_after_verified_warp(self, state, receipt):
        """Rebase branch-local proxy identities after the controller proves a warp."""
        self.align(receipt)
        marker = state.get('registryWarpRebranch') or {}
        require(marker.get('active') is True and marker.get('frame') == state.get('frame')
                and marker.get('nativeStateChanged') is False,
                'Controller did not publish a verified non-mutating registry warp rebranch receipt')
        previous = copy.deepcopy(self.proxies)
        self.proxies = {}
        captured = self.capture(state, receipt)
        return {
            'frame': state['frame'],
            'previousProxyIds': sorted(previous),
            'currentProxyIds': sorted(self.proxies),
            'captured': captured,
            'controllerReceipt': copy.deepcopy(marker),
            'nativeStateChanged': False,
            'scope': ('Probe bookkeeping only: proxy instance identities from the abandoned future are discarded; '
                      'current identities are recaptured from the verified restored graph and native bodies.'),
        }

    def requests(self, state, receipt):
        self.align(receipt)
        live = {e['id']: e for e in state['entities'] if e.get('exists')}
        metadata = {r['EntityId']: r for r in state['registry']}
        foods = {e['id']: e for e in receipt['detail']['entities']}
        bodies = {b['entityId']: b for b in native_physics(receipt)['bodies']}
        requests = []
        for eid, entity in live.items():
            meta = metadata[eid]; food = foods.get(eid, {})
            if (len(entity['path']) == 2 and 'ServerWorkableItem' in (meta.get('Components') or [])
                    and food.get('workStages') == 8 and food.get('composition') is None
                    and meta.get('SpawnNames') is None):
                parent = metadata.get(entity['path'][0], {})
                require(meta.get('Name') in (parent.get('SpawnNames') or []) and food.get('name') == meta.get('Name'),
                        'Raw metadata refresh lacks observed crate/object provenance')
                requests.append({'operation': 'refresh', 'args': {'entityId': eid},
                                 'prior': {'frame': state['frame'], 'path': entity['path'], 'name': meta['Name']}})
        removed = set((state.get('graphMappingValidation') or {}).get('removedNativeIds') or [])
        removed.update(row.get('nativeId') for row in state.get('observedProxyRetirements', [])
                       if type(row.get('nativeId')) is int)
        for eid, prior in self.proxies.items():
            if eid in bodies or eid in removed:
                continue
            require(eid not in live and prior['ownerId'] not in live
                    and not any(e['path'] == prior['logicalPath'] for e in live.values()),
                    'Missing proxy still has a live owner; absence reconciliation is not admitted')
            require(not any(b['bodyInstanceId'] == prior['bodyInstanceId'] for b in bodies.values()),
                    'Prior proxy body was observed under another current entity ID')
            require(state['frame'] >= prior['frame'], 'Proxy absence predates its captured identity')
            requests.append({'operation': 'observe-absent', 'args': {'entityId': eid,
                             'sceneMetadataRefreshes': self.key[2], 'priorBodyInstanceId': prior['bodyInstanceId']},
                             'prior': copy.deepcopy(prior)})
        require(len(requests) <= 4, 'First-dish metadata repair exceeded its bounded scope')
        return requests

    def capture(self, state, receipt):
        self.align(receipt)
        validate_observed_layout(state)
        graph = state.get('graphMappingValidation') or {}
        if not graph.get('ok'):
            return []
        require(graph.get('frame') == state['frame'], 'Proxy capture graph receipt is stale')
        live = {e['id']: e for e in state['entities'] if e.get('exists')}
        metadata = {r['EntityId']: r for r in state['registry']}
        bodies = {b['entityId']: b for b in native_physics(receipt)['bodies']}
        mapped = {tuple(r['path']): r['id'] for r in graph.get('spawnedMappings', [])}
        captured = []
        for row in graph.get('observedPhysicalContainers', []):
            path = row['logicalPath']; eid = row['nativeId']
            if len(path) <= 1:
                continue  # Never reconcile an original plate's fixed proxy.
            owner = mapped.get(tuple(path)); meta = metadata.get(eid, {})
            require(owner in live and live[owner]['path'] == path and eid in bodies and eid not in live,
                    'Dynamic proxy lacks its exact live native graph/body association')
            require({'Rigidbody', 'ObjectContainer'} <= set(meta.get('Components') or [])
                    and 47 in (meta.get('SyncEntityTypes') or []), 'Dynamic proxy lacks native codec/component proof')
            item = {'entityId': eid, 'bodyInstanceId': bodies[eid]['bodyInstanceId'], 'ownerId': owner,
                    'logicalPath': path, 'frame': state['frame'], 'epoch': list(self.key),
                    'graphRegistrySha256': graph.get('registrySha256'),
                    'bodySha256': hashlib.sha256(json.dumps(bodies[eid], sort_keys=True).encode()).hexdigest()}
            old = self.proxies.get(eid)
            require(old is None or (old['bodyInstanceId'], old['ownerId'], old['logicalPath']) ==
                    (item['bodyInstanceId'], owner, path), 'Dynamic proxy incarnation changed within the same native epoch')
            self.proxies[eid] = item
            captured.append(item)
        return captured

    @staticmethod
    def validate_publication(request, response):
        result = result_of(response); args = request['args']; eid = args['entityId']
        require(result.get('entityId') == eid, 'Registry publication targeted a different entity')
        if request['operation'] == 'refresh':
            entries = result.get('entries') or []
            require(result.get('published') == 1 and len(entries) == 1 and entries[0].get('id') == eid
                    and entries[0].get('hasSpawnCollection') is True
                    and isinstance(entries[0].get('spawnNames'), list) and len(entries[0]['spawnNames']) == 1,
                    'Raw workable refresh did not observe its native ordered next-prefab list')
        else:
            require(result.get('sceneMetadataRefreshes') == args['sceneMetadataRefreshes']
                    and result.get('priorBodyInstanceId') == args['priorBodyInstanceId']
                    and result.get('source') == 'observed-native-registry-absence'
                    and result.get('historicalRemovalEventObserved') is False
                    and result.get('frameworkOnlyRetirementPublished') is True,
                    'Dynamic absence publication lacks its exact current-absence proof')
            require(eid not in result.get('currentRegisteredIds', [eid])
                    and args['priorBodyInstanceId'] not in result.get('currentRegisteredBodyInstances', [args['priorBodyInstanceId']]),
                    'Published absence receipt still contains the native ID/body')
        return result

    @staticmethod
    def received(request, publication, state):
        eid = request['args']['entityId']
        if request['operation'] == 'refresh':
            rows = [r for r in state['registry'] if r['EntityId'] == eid]
            return len(rows) == 1 and rows[0].get('SpawnNames') == publication['entries'][0]['spawnNames']
        graph_removed = (state.get('graphMappingValidation') or {}).get('removedNativeIds') or []
        explicit = [row.get('nativeId') for row in state.get('observedProxyRetirements', [])]
        return eid in graph_removed or eid in explicit
