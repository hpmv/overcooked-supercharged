"""Read-only native held-plate wait and original-stove fryer harvest proof."""
import argparse
import gzip
import json
from pathlib import Path
from check_bowl_offmix import ingredients, nodes, ordinary_request, controller_evidence, pin, require
from check_flavor_throw import Checker as FlavorChecker, JOBS


def cooked(entity):
    food = entity.get('composition')
    return (ingredients(food) == [16620, 18448, 22804]
            and any(n.get('type') == 'CookedCompositeAssembledNode' and n.get('state') == 'Cooked'
                    and n.get('cookingStepId') == 17160 for n in nodes(food))
            and any(n.get('type') == 'MixedCompositeAssembledNode' and n.get('state') == 'Mixed' for n in nodes(food))
            and not any(n.get('state') in ['Burnt', 'OverMixed', 'Ruined'] for n in nodes(food)))


class Checker(FlavorChecker):
    def __init__(self):
        super().__init__('Chocolate')
        self.fixed = {}; self.held_wait = []; self.cooked_frame = self.harvest_frame = None
        self.previous = None; self.edge = None

    def resolve(self, state):
        super().resolve(state)
        def station(component, x, z):
            rows = [e for e in state['entities'] if e.get('active') and component in e.get('components', [])
                    and abs(e['position']['x'] - x) < .05 and abs(e['position']['z'] - z) < .05]
            require(len(rows) == 1, 'Original fryer/plate/output station is not unique')
            return rows[0]
        home = station('CookingStation', 22.8, -21.6)
        source = station('AttachStation', 19.2, -15.6)
        output = station('AttachStation', 25.2, -20.4)
        plate = self.entity(state, source['attachedEntityId'])
        require(home['attachedEntityId'] == self.mapping['basket'] and plate and 'Plate' in plate['components']
                and not ingredients(plate.get('composition')) and output['attachedEntityId'] == 0,
                'Fresh original fryer, clean plate or empty output is absent')
        self.mapping.update(fryerHome=home['id'], plateSource=source['id'], plate=plate['id'], output=output['id'])
        self.fixed = {e['id']: e['observedOrdinal'] for e in [home, source, output, plate]}

    def state(self, state, inputs):
        prior = self.last
        super().state(state, inputs)
        m = self.mapping; basket = self.entity(state, m['basket']); plate = self.entity(state, m['plate'])
        chef = next(c for c in state['chefs'] if c['playerId'] == 3)
        for identity, ordinal in self.fixed.items():
            require(self.entity(state, identity) and self.entity(state, identity)['observedOrdinal'] == ordinal,
                    'Original plate or station identity changed')
        require(self.entity(state, m['fryerHome'])['attachedEntityId'] == m['basket']
                and not any(c['heldEntityId'] == m['basket'] for c in state['chefs']),
                'Basket moved from its original native stove')
        require(basket['cookingTime'] == 10 and basket['cookingProgress'] < 19, 'Native fryer duration or harvest deadline changed')
        frame = state['gameplayFrame']
        held = chef['heldEntityId'] == m['plate']
        pad = next(p for p in inputs if p['player'] == 3)
        if held and not ingredients(plate.get('composition')) and self.mixed(basket) and not cooked(basket):
            if all(pad[k] == 0 for k in ['x', 'y']) and not any(pad[k] for k in ['pickup', 'use', 'dash']):
                self.held_wait.append((frame, state['timer'], basket['cookingProgress']))
        if held and cooked(basket) and self.cooked_frame is None:
            require(self.held_wait, 'No earlier native cooking wait while holding the original clean plate')
            self.cooked_frame = frame
        if cooked(plate) and self.harvest_frame is None:
            require(self.cooked_frame is not None and held and not ingredients(basket.get('composition')),
                    'Exact plate did not consume the previously Cooked original basket')
            require(prior is not None and cooked(self.entity(prior, m['basket'])), 'Native consumption predecessor is missing')
            oldpad = next(p for p in self.previous_inputs if p['player'] == 3)
            oldchef = next(c for c in prior['chefs'] if c['playerId'] == 3)
            require(pad['pickup'] and not oldpad['pickup'] and oldchef.get('placementTargetId') in [m['basket'], m['fryerHome']],
                    'Exact fryer consumption lacks its targeted fresh pickup edge')
            self.harvest_frame = frame
            self.edge = {'frame': frame, 'target': oldchef['placementTargetId'], 'plate': m['plate'], 'basket': m['basket']}
        if self.harvest_frame is not None:
            require(cooked(plate) and not ingredients(basket.get('composition')), 'Harvested recipe changed or fryer was refilled')
        self.previous_inputs = inputs

    def finish(self):
        require(self.done == JOBS and self.work_frames > 0 and self.release and self.caught_frame is not None
                and self.throw_complete and self.native_mixed and self.transferred, 'Native preparation chain is incomplete')
        require(self.harvest_frame is not None and len(self.held_wait) > 2, 'Held-plate wait/harvest is incomplete')
        state = self.last; m = self.mapping
        require(self.entity(state, m['output'])['attachedEntityId'] == m['plate'] and cooked(self.entity(state, m['plate'])),
                'Exact finished plate is not on its selected native output')
        require(self.entity(state, m['home'])['attachedEntityId'] == m['bowl']
                and not ingredients(self.entity(state, m['bowl']).get('composition')), 'Original empty bowl was not restored')
        require(all(c['controlsEnabled'] and c['heldEntityId'] == 0 for c in state['chefs'])
                and all(p['x'] == p['y'] == 0 and not any(p[k] for k in ['pickup', 'use', 'dash']) for p in self.inputs),
                'Final chefs are not controlled, empty and neutral')
        return {'passed': True, 'classification': 'One native mechanism probe; adaptive admission, performance and replay are separate',
                'mapping': m, 'nativeCookedFrame': self.cooked_frame, 'consumption': self.edge,
                'neutralHeldPlateWaitSamples': len(self.held_wait), 'firstWait': self.held_wait[0], 'lastWait': self.held_wait[-1],
                'nativeWaitSeconds': self.held_wait[0][1] - self.held_wait[-1][1], 'finalFrame': state['gameplayFrame'],
                'finalNativePlateComposition': self.entity(state, m['plate'])['composition'],
                'originalBasketNeverMoved': True, 'finalScore': state['score']}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for name in ['trace', 'plan', 'result', 'output']: p.add_argument(name, type=Path)
    p.add_argument('--controller-bundle', type=Path, required=True)
    args = p.parse_args(); require(not args.output.exists(), 'Proof output already exists')
    plan = json.loads(args.plan.read_text(encoding='utf-8-sig'))
    require([j['id'] for j in plan['jobs']] == JOBS, 'Unexpected authored probe')
    evidence = controller_evidence(args.controller_bundle); before = pin(args.trace); checker = Checker()
    calls = 0; previous = None; plugin = manifest = None
    with gzip.open(args.trace, 'rt', encoding='utf-8-sig') as stream:
        for line in stream:
            row = json.loads(line)
            if row.get('kind') == 'event': checker.event(row['name'], row.get('value') or {})
            elif row.get('kind') == 'call':
                calls += 1; request, response = row['request'], row['response']; ordinary_request(request)
                require(response.get('ok') and (request['command'] != 'restart' or calls == 1), 'Failed call or mid-probe restart')
                s = response['state']; frame = s['gameplayFrame']
                require(previous is None or frame == previous + (request['command'] == 'step'), 'Missing native input frame')
                previous = frame; ins = s['instrumentation']
                if plugin is None: plugin, manifest = ins['manifest']['pluginSha256'], ins['manifestSha256']
                require(plugin == ins['manifest']['pluginSha256'] and manifest == ins['manifestSha256'] and s['captureFramerate'] == 60
                        and s['fixedDeltaTime'] == .02 and not s['timerSuppressed'], 'Native instrumentation/timing changed')
                checker.state(s, response['inputs'])
    result = json.loads(args.result.read_text(encoding='utf-8-sig'))
    require(result.get('ok') and result.get('paused') and result.get('state') == checker.last, 'Closed native result differs')
    require(before == pin(args.trace), 'Trace changed during checking')
    proof = checker.finish(); proof.update(sourceTrace=before, route=pin(args.plan), result=pin(args.result), calls=calls,
        checkerSource=pin(Path(__file__)), supportingCheckers=[pin(Path(__file__).with_name(n)) for n in ['check_flavor_throw.py', 'check_bowl_offmix.py']],
        controllerBundleEvidence=evidence, pluginSha256=plugin, instrumentationManifestSha256=manifest)
    args.output.write_text(json.dumps(proof, indent=2)); print(json.dumps({k:proof[k] for k in ['passed','nativeCookedFrame','consumption','finalFrame']}))


if __name__ == '__main__': main()
