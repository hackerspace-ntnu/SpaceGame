#!/usr/bin/env node
// Prints the JS palette as one #RRGGBB per line, so tools/palette_preview.py --check can
// compare the browser port against the Python one. Lives in tools/ rather than in
// LookLab/app/ so the served app stays free of Node-only code.
import { DEFAULT_SHAPE, buildPalette, hexOf } from '../LookLab/app/palette.js';

for (const color of buildPalette(DEFAULT_SHAPE)) {
  console.log(hexOf(color));
}
