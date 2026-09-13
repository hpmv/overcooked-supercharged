"""Create a neutral, sparsely observed native round-end diagnostic, not a TAS movie."""
import json
from pathlib import Path

destination = Path(__file__).resolve().parents[1] / 'routes/probes/native-round-end.jsonl'
neutral = [{'player': p, 'x': 0, 'y': 0, 'pickup': False, 'use': False, 'dash': False} for p in range(4)]
requests = [{'version': 1, 'command': 'load', 'seed': 0, 'isolateRecipeRandom': True},
            {'version': 1, 'command': 'render', 'width': 1280, 'height': 720, 'renderRate': 0}]
requests += [{'version': 1, 'command': 'step', 'steps': 600, 'inputs': neutral} for _ in range(28)]
requests += [{'version': 1, 'command': 'inspect'}]
with destination.open('x', encoding='utf-8') as stream:
    for request in requests:
        stream.write(json.dumps(request, separators=(',', ':')) + '\n')
print(str(destination))
