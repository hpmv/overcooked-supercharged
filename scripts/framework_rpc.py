"""Run a bounded script against the framework bridge and headless host.

Both connections stay open for the script. Closing an owning bridge connection
releases inputs and pauses the game. No simulation state is silently restored.
"""
import argparse
import http.client
import json
from pathlib import Path
import socket
import struct
import time


class Client:
    def __init__(self, port):
        self.socket = socket.create_connection(('127.0.0.1', port), timeout=10)
        self.socket.settimeout(15)
        self.socket.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        # Read-only native trace exports can legitimately exceed 32 MiB after
        # adding bounded Animator records. The bridge remains local and frames
        # every response with a uint32 length; keep a finite diagnostic ceiling
        # while allowing one complete 32,768-event export.
        self.max_response_bytes = 64 * 1024 * 1024

    def read(self, count):
        chunks = bytearray()
        while len(chunks) < count:
            data = self.socket.recv(count - len(chunks))
            if not data:
                raise ConnectionError('RPC peer closed the connection')
            chunks.extend(data)
        return bytes(chunks)

    def call(self, request):
        data = json.dumps({'version': 1, **request}, separators=(',', ':')).encode()
        self.socket.sendall(struct.pack('<I', len(data)) + data)
        size, = struct.unpack('<I', self.read(4))
        if not 0 < size <= self.max_response_bytes:
            raise ValueError(f'Invalid RPC size {size}')
        result = json.loads(self.read(size))
        if result.get('ok') is False:
            raise RuntimeError(json.dumps(result))
        return result

    def close(self):
        self.socket.close()


class ControllerClient:
    def __init__(self, port):
        self.connection = http.client.HTTPConnection('127.0.0.1', port, timeout=15)

    def call(self, request):
        self.connection.request('POST', '/', json.dumps(request), {'Content-Type': 'application/json'})
        response = self.connection.getresponse()
        result = json.loads(response.read())
        if response.status != 200 or result.get('ok') is False:
            raise RuntimeError(json.dumps(result))
        return result

    def close(self):
        self.connection.close()

    def status(self):
        """Explicit V8+ lightweight poll; full boundary inspection stays separate."""
        return self.call({'command': 'status'})


def at(value, path):
    for key in path.split('.'):
        value = value[int(key)] if isinstance(value, list) else value[key]
    return value


def satisfied(value, condition):
    if 'all' in condition:
        return all(satisfied(value, child) for child in condition['all'])
    return at(value, condition['path']) == condition['equals']


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--script', type=Path)
    parser.add_argument('--out', type=Path)
    parser.add_argument('--bridge-port', type=int, default=17636)
    parser.add_argument('--controller-port', type=int, default=17637)
    args = parser.parse_args()
    steps = json.loads(args.script.read_text(encoding='utf-8-sig')) if args.script else [
        {'target': 'bridge', 'request': {'command': 'status'}}]
    clients, records = {}, []
    start = time.monotonic()
    try:
        for index, step in enumerate(steps):
            target = step['target']
            if target not in ('bridge', 'controller'):
                raise ValueError(f'Unknown RPC target {target}')
            if target not in clients:
                clients[target] = Client(args.bridge_port) if target == 'bridge' else ControllerClient(args.controller_port)
            deadline = time.monotonic() + step.get('timeoutSeconds', 30)
            attempts = 0
            while True:
                response = clients[target].call(step['request'])
                attempts += 1
                condition = step.get('until')
                if condition is None or satisfied(response, condition):
                    break
                if time.monotonic() >= deadline:
                    raise TimeoutError(f'Step {index} timed out: {json.dumps(response)}')
                time.sleep(0.1)
            row = {'index': index, 'target': target, 'request': step['request'], 'response': response,
                   'attempts': attempts, 'elapsedSeconds': time.monotonic() - start}
            records.append(row)
            print(json.dumps(row, separators=(',', ':')), flush=True)
    finally:
        for client in clients.values():
            client.close()
        if args.out:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_text(json.dumps({'steps': records, 'elapsedSeconds': time.monotonic() - start}, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
