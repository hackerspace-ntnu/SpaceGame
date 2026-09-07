// WebGL2 for the Look Lab: one context, one program, one viewport draw per panel.
//
// One canvas rather than one per scene, because a browser caps live WebGL contexts and
// six panels plus a pinned A/B comparison would sit near that cap. Panels are drawn into
// their own viewport rects on the single canvas instead.
//
// Nothing here runs on a timer: these are stills, so the app calls draw() when something
// changed and an idle stack costs nothing.

const VERTEX_SOURCE = `#version 300 es
// A fullscreen triangle from gl_VertexID — no vertex buffer, no attribute state.
out vec2 vUv;
void main() {
  vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  vUv = p;
  gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
}`;

const FRAGMENT_PRELUDE = `#version 300 es
precision highp float;
precision highp int;
precision highp sampler2D;

in vec2 vUv;
out vec4 fragColor;

uniform sampler2D uSource;
uniform sampler2D uPalette;   // column i: row 0 linear RGB, row 1 Oklab
uniform int uPaletteCount;

// Must stay in lockstep with PastelPalette.LinearToOklab and the HLSL shader.
vec3 linearToOklab(vec3 c) {
  vec3 lms = vec3(
    dot(c, vec3(0.4122214708, 0.5363325363, 0.0514459929)),
    dot(c, vec3(0.2119034982, 0.6806995451, 0.1073969566)),
    dot(c, vec3(0.0883024619, 0.2817188376, 0.6299787005)));
  lms = pow(max(lms, 0.0), vec3(1.0 / 3.0));
  return vec3(
    dot(lms, vec3(0.2104542553,  0.7936177850, -0.0040720468)),
    dot(lms, vec3(1.9779984951, -2.4285922050,  0.4505937099)),
    dot(lms, vec3(0.0259040371,  0.7827717662, -0.8086757660)));
}

// The canvas is untagged, so the page encodes on the way out the same way the swapchain
// does for the game. The standard exponent, not Unity's 0.41666 approximation: this is
// display encoding, not a palette value.
vec3 linearToSrgb(vec3 c) {
  c = clamp(c, 0.0, 1.0);
  return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(0.0031308, c));
}
`;

const FRAGMENT_MAIN = `
void main() {
  // uSource is SRGB8_ALPHA8, so the sample is already linear — the exact inverse of the
  // sRGB encode Unity's capture render target applied.
  vec4 source = texture(uSource, vUv);
  fragColor = vec4(linearToSrgb(applyStages(source.rgb, vUv)), source.a);
}
`;

export class LabRenderer {
  constructor(canvas) {
    this.gl = canvas.getContext('webgl2', {
      alpha: false,
      antialias: false,
      preserveDrawingBuffer: true, // so the parity harness in phase 2 can read the canvas
    });
    if (!this.gl) throw new Error('This browser has no WebGL2. The lab needs it.');
    this.canvas = canvas;
    this.program = null;
    this.uniforms = new Map();
    this.paletteTexture = null;
    this.paletteCount = 0;
    this.sources = new Map();
    this.vao = this.gl.createVertexArray(); // WebGL2 refuses to draw with no VAO bound
  }

  /** Compiles the stages into one fragment shader, applied in the order given.
   *  Milliseconds; a slider drag does not come through here, it only sets a uniform.
   *
   *  Each body declares `vec3 apply(vec3, vec2)`, so every body past the first would
   *  redefine the same symbol and the link would fail. They are renamed per stage and
   *  called in sequence instead — which is also what makes a stage's position in the
   *  chain meaningful: grain has to reach the frame before the snap quantises it.
   */
  compile(stages, paramNames) {
    const gl = this.gl;
    const declarations = paramNames.map((n) => `uniform float P_${n};`).join('\n');

    const bodies = stages.map((stage, index) => {
      const renamed = stage.body.replace(/\bvec3\s+apply\s*\(/, `vec3 apply_${index}(`);
      if (renamed === stage.body) {
        throw new Error(`${stage.id}: no 'vec3 apply(vec3 c, vec2 uv)' in the stage body`);
      }
      return renamed;
    });
    const chain = stages
      .map((_, index) => `  c = apply_${index}(c, uv);`)
      .join('\n');
    const applyStages =
      `\nvec3 applyStages(vec3 c, vec2 uv) {\n${chain}\n  return c;\n}\n`;

    const source =
      FRAGMENT_PRELUDE + declarations + '\n' + bodies.join('\n') + applyStages + FRAGMENT_MAIN;

    const program = link(gl, VERTEX_SOURCE, source);
    if (this.program) gl.deleteProgram(this.program);
    this.program = program;

    this.uniforms.clear();
    const count = gl.getProgramParameter(program, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < count; i++) {
      const name = gl.getActiveUniform(program, i).name.replace(/\[0\]$/, '');
      this.uniforms.set(name, gl.getUniformLocation(program, name));
    }
  }

  /** Uploads one captured still. Kept by name so switching panels costs nothing. */
  setSource(name, image) {
    const gl = this.gl;
    let texture = this.sources.get(name);
    if (!texture) {
      texture = gl.createTexture();
      this.sources.set(name, texture);
    }
    gl.bindTexture(gl.TEXTURE_2D, texture);
    // SRGB8_ALPHA8 so the hardware decode is the exact inverse of the sRGB encode the
    // capture applied. Doing it in GLSL instead would be a second approximation.
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.SRGB8_ALPHA8, gl.RGBA, gl.UNSIGNED_BYTE, image);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  }

  /** `data` is the width x 2 RGBA32F payload from palette.js. */
  setPalette(data, width, count) {
    const gl = this.gl;
    if (!this.paletteTexture) this.paletteTexture = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.paletteTexture);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA32F, width, 2, 0, gl.RGBA, gl.FLOAT, data);
    // NEAREST throughout: entries are looked up by index with texelFetch, and filtering
    // between two palette entries would smear exactly the hard edges the snap creates.
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    this.paletteCount = count;
  }

  /** `panels` is [{ scene, x, y, width, height }] in CSS pixels from the top left. */
  draw(panels, params) {
    const gl = this.gl;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const width = Math.round(this.canvas.clientWidth * dpr);
    const height = Math.round(this.canvas.clientHeight * dpr);
    if (this.canvas.width !== width || this.canvas.height !== height) {
      this.canvas.width = width;
      this.canvas.height = height;
    }

    gl.bindVertexArray(this.vao);
    gl.useProgram(this.program);
    gl.disable(gl.DEPTH_TEST);
    gl.disable(gl.BLEND);
    gl.viewport(0, 0, width, height);
    gl.clearColor(0, 0, 0, 1);
    gl.clear(gl.COLOR_BUFFER_BIT);

    gl.activeTexture(gl.TEXTURE1);
    gl.bindTexture(gl.TEXTURE_2D, this.paletteTexture);
    this.setUniform('uPalette', (l) => gl.uniform1i(l, 1));
    this.setUniform('uPaletteCount', (l) => gl.uniform1i(l, this.paletteCount));
    this.setUniform('uSource', (l) => gl.uniform1i(l, 0));

    for (const [name, value] of Object.entries(params)) {
      this.setUniform(`P_${name}`, (l) => gl.uniform1f(l, value));
    }

    for (const panel of panels) {
      const texture = this.sources.get(panel.scene);
      if (!texture) continue;
      gl.activeTexture(gl.TEXTURE0);
      gl.bindTexture(gl.TEXTURE_2D, texture);
      // GL's origin is bottom-left; the panel rects are top-left like the DOM.
      gl.viewport(
        Math.round(panel.x * dpr),
        height - Math.round((panel.y + panel.height) * dpr),
        Math.round(panel.width * dpr),
        Math.round(panel.height * dpr));
      gl.drawArrays(gl.TRIANGLES, 0, 3);
    }
  }

  setUniform(name, set) {
    const location = this.uniforms.get(name);
    // Absent is normal, not an error: GLSL drops any uniform the stage body never reads.
    if (location !== undefined && location !== null) set(location);
  }
}

function link(gl, vertexSource, fragmentSource) {
  const program = gl.createProgram();
  const vertex = compileShader(gl, gl.VERTEX_SHADER, vertexSource);
  const fragment = compileShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
  gl.attachShader(program, vertex);
  gl.attachShader(program, fragment);
  gl.linkProgram(program);
  gl.deleteShader(vertex);
  gl.deleteShader(fragment);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const log = gl.getProgramInfoLog(program);
    gl.deleteProgram(program);
    throw new Error('Link failed: ' + log);
  }
  return program;
}

function compileShader(gl, type, source) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    // Numbered, because a stage body is concatenated into a prelude and the raw line
    // number in the driver's message means nothing on its own.
    const numbered = source.split('\n').map((l, i) => `${i + 1}: ${l}`).join('\n');
    const log = gl.getShaderInfoLog(shader);
    gl.deleteShader(shader);
    throw new Error(`Compile failed: ${log}\n${numbered}`);
  }
  return shader;
}
