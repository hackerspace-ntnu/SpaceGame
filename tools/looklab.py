#!/usr/bin/env python3
"""Serves the Look Lab and takes its writes.

The page and the Unity Editor never meet. The page POSTs to this server, which writes
LookLab/live/look.json atomically; LookLabLive.cs polls that file's timestamp and mutates
the live render feature. Two halves that never connect, so there is no socket in the
Editor to leak across a domain reload.

    python3 tools/looklab.py serve            # http://127.0.0.1:8777/app/
    python3 tools/looklab.py serve --port N

Bound to the loopback interface only: this writes files in the repository on an
unauthenticated request, which is fine for a local dev tool and not fine on a shared
network.
"""
import argparse
import json
import os
import pathlib
import re
import sys
import tempfile
from http.server import HTTPServer, SimpleHTTPRequestHandler
from urllib.parse import unquote

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOOKLAB = ROOT / 'LookLab'
LIVE_LOOK = LOOKLAB / 'live' / 'look.json'
PRESETS = LOOKLAB / 'presets'
SCENES = LOOKLAB / 'scenes'
STAGES = LOOKLAB / 'stages'

# Preset names become filenames, so they are an allowlist rather than a sanitiser: a
# deny-list of traversal sequences is a fight you lose eventually.
NAME = re.compile(r'^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}$')

MAX_BODY_BYTES = 256 * 1024

# Mirrors PaletteShape.Default in
# Assets/Game/Scripts/World/Environment/ColorGrade/PaletteShape.cs. Only used to write a
# valid look.json the first time; the lab reads its own defaults from the stage manifest.
DEFAULT_LOOK = {
    'enabled': True,
    'blend': 1.0,
    'palette': {
        'hueCount': 16,
        'lightnesses': [0.92, 0.82, 0.72, 0.61, 0.49, 0.36],
        'chromaFractions': [0.5, 1.0],
        'chromaCeiling': 0.20,
        'neutralCount': 12,
        'neutralMinL': 0.16,
        'neutralMaxL': 0.97,
    },
}


def write_atomically(path, text):
    """Writes via a tempfile in the same directory plus os.replace — a true rename, so a
    reader can never see a half-written file. LookLabLive polls at 20 Hz and would
    otherwise eventually catch one mid-write and log a parse error."""
    path.parent.mkdir(parents=True, exist_ok=True)
    handle = tempfile.NamedTemporaryFile(
        'w', dir=str(path.parent), prefix=path.name + '.', delete=False)
    try:
        with handle:
            handle.write(text)
        os.replace(handle.name, str(path))
    except BaseException:
        # Leaving a stray temp file beside look.json would confuse the next reader more
        # than the failure itself does.
        pathlib.Path(handle.name).unlink(missing_ok=True)
        raise


def list_scenes():
    """Every capture pack that actually has a frame in it."""
    scenes = []
    if not SCENES.is_dir():
        return scenes

    for directory in sorted(SCENES.iterdir()):
        if not (directory / 'color_ldr.png').is_file():
            continue
        pack = {}
        pack_path = directory / 'pack.json'
        if pack_path.is_file():
            try:
                pack = json.loads(pack_path.read_text())
            except json.JSONDecodeError as error:
                # Loud, not silent: a pack whose metadata is unreadable is exactly the
                # pack that will later be blamed for not matching the build.
                print('looklab: %s is not valid JSON (%s)' % (pack_path, error),
                      file=sys.stderr)
        scenes.append({'name': directory.name, 'pack': pack})

    return scenes


class Handler(SimpleHTTPRequestHandler):
    """Static files out of LookLab/, plus the handful of writes the lab needs."""

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(LOOKLAB), **kwargs)

    def log_message(self, fmt, *args):
        # One line per slider drag would bury anything worth reading.
        if not self.path.startswith('/api/look'):
            super().log_message(fmt, *args)

    def do_GET(self):
        if self.path == '/':
            self.send_response(302)
            self.send_header('Location', '/app/')
            self.end_headers()
            return
        if self.path == '/api/scenes':
            self.send_json(list_scenes())
            return
        if self.path == '/api/stages':
            names = sorted(p.name for p in STAGES.glob('*.stage')) if STAGES.is_dir() else []
            self.send_json(names)
            return
        if self.path == '/api/presets':
            names = sorted(p.stem for p in PRESETS.glob('*.json')) if PRESETS.is_dir() else []
            self.send_json(names)
            return
        super().do_GET()

    def do_POST(self):
        if self.path != '/api/look':
            self.send_error(404)
            return
        body = self.read_json()
        if body is None:
            return
        write_atomically(LIVE_LOOK, json.dumps(body, indent=2))
        self.send_json({'written': str(LIVE_LOOK.relative_to(ROOT))})

    def do_PUT(self):
        name = self.preset_name()
        if name is None:
            return
        body = self.read_json()
        if body is None:
            return
        write_atomically(PRESETS / (name + '.json'), json.dumps(body, indent=2))
        self.send_json({'saved': name})

    def do_DELETE(self):
        name = self.preset_name()
        if name is None:
            return
        path = PRESETS / (name + '.json')
        if not path.is_file():
            self.send_error(404, 'no preset named ' + name)
            return
        path.unlink()
        self.send_json({'deleted': name})

    def preset_name(self):
        prefix = '/api/presets/'
        if not self.path.startswith(prefix):
            self.send_error(404)
            return None
        name = unquote(self.path[len(prefix):])
        if not NAME.match(name):
            self.send_error(400, 'preset names are letters, digits, space, - and _ '
                                 '(1-64 characters)')
            return None
        return name

    def read_json(self):
        try:
            length = int(self.headers.get('Content-Length', '0'))
        except ValueError:
            self.send_error(400, 'bad Content-Length')
            return None
        if length <= 0 or length > MAX_BODY_BYTES:
            self.send_error(400, 'body must be 1..%d bytes' % MAX_BODY_BYTES)
            return None
        try:
            return json.loads(self.rfile.read(length))
        except json.JSONDecodeError as error:
            self.send_error(400, 'not JSON: %s' % error)
            return None

    def send_json(self, payload):
        encoded = json.dumps(payload).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)


def serve(port):
    for directory in (LOOKLAB / 'live', PRESETS, SCENES, STAGES):
        directory.mkdir(parents=True, exist_ok=True)
    if not LIVE_LOOK.exists():
        write_atomically(LIVE_LOOK, json.dumps(DEFAULT_LOOK, indent=2))
        print('looklab: wrote a default %s' % LIVE_LOOK.relative_to(ROOT))

    if not list_scenes():
        print('looklab: no scene packs yet - capture some with '
              'SpaceGame > Look Lab > Capture Scene Pack')

    server = HTTPServer(('127.0.0.1', port), Handler)
    print('looklab: http://127.0.0.1:%d/app/  (ctrl-c to stop)' % port)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print()
    finally:
        server.server_close()
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command')
    serve_parser = sub.add_parser('serve', help='serve the lab on localhost')
    serve_parser.add_argument('--port', type=int, default=8777)
    args = parser.parse_args()

    if args.command != 'serve':
        parser.print_help()
        return 2
    return serve(args.port)


if __name__ == '__main__':
    sys.exit(main())
