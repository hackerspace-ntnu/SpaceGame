# Cave / Alien Asset Pack for Unity

13 medium-poly sculpted models exported as FBX, organized by category. All meshes have:
- Triangle counts within a 300–800 budget
- Smart-projected UVs (one island per object)
- A placeholder Principled BSDF material with base color + (where appropriate) emission
- Origin pivots set for natural placement (see below)
- Unity-friendly axis orientation baked into the FBX (`-Z` forward, `Y` up)

## Folder layout

```
UnityAssets/
├── Stalagmites/
│   ├── Stalagmite_01.fbx   (720 tris, base pivot)
│   └── Stalagmite_02.fbx   (720 tris, base pivot)
├── Stalactites/
│   ├── Stalactite_01.fbx   (720 tris, top pivot)
│   └── Stalactite_02.fbx   (720 tris, top pivot)
├── Boulders/
│   ├── Boulder_01.fbx      (480 tris, base pivot)
│   ├── Boulder_02.fbx      (480 tris, base pivot)
│   ├── Boulder_03.fbx      (480 tris, base pivot)
│   └── Boulder_04.fbx      (480 tris, base pivot)
├── AlienFungus/
│   ├── BioluminescentFrond_01.fbx  (380 tris, base pivot)
│   ├── SporePodCluster_01.fbx      (508 tris, top pivot)
│   ├── CrystalLattice_01.fbx       (336 tris, base pivot)
│   ├── PulsingNodule_01.fbx        (320 tris, base pivot)
│   └── AlienMossPatch_01.fbx       (392 tris, base pivot)
└── Source_UnityAssets.blend
```

## Pivot conventions

| Model type            | Pivot location | Why                                                            |
|-----------------------|----------------|----------------------------------------------------------------|
| Stalagmite            | Base (min Z)   | Drop onto floors; aligns to surface normal at ground point     |
| Stalactite            | Top (max Z)    | Attach to ceiling at the ceiling-surface point                 |
| Boulder               | Base (min Z)   | Sits on the floor                                              |
| Bioluminescent Frond  | Base           | Plants into the ground at base                                 |
| Spore Pod Cluster     | Top            | Hangs from a ceiling attachment point                          |
| Crystal Lattice       | Base           | Grows up from a surface                                        |
| Pulsing Nodule        | Base           | Sits on a surface (typically near liquid)                      |
| Alien Moss Patch      | Base           | Lies on the ground                                             |

## Materials

Placeholder materials only — designed to be replaced or extended in Unity once you texture.

| Material            | Base color (sRGB)    | Roughness | Metallic | Emission                              |
|---------------------|----------------------|-----------|----------|----------------------------------------|
| StoneMat            | grey-brown #6B6661   | 0.92      | 0.0      | none                                   |
| BoulderMat          | darker stone #5C5751 | 0.95      | 0.0      | none                                   |
| BioFrondMat         | dark teal #0D7F73    | 0.70      | 0.0      | cyan #33FFD9 @ 3.5                     |
| SporePodMat         | dusty rose #8C3366   | 0.55      | 0.0      | pink #D9408C @ 0.6                     |
| CrystalMat          | violet #8C59E5       | 0.25      | 0.0      | violet #A666FF @ 2.2                   |
| PulsingNoduleMat    | flesh red #A62E38    | 0.45      | 0.0      | red #FF4D4D @ 1.4                      |
| AlienMossMat        | mossy teal #2D7359  | 0.85      | 0.0      | teal #40A680 @ 0.4                     |

## Unity import settings (recommended)

For each FBX in the Inspector:

- **Model tab**
  - Scale Factor: 1
  - Convert Units: ✅
  - Mesh Compression: Off (low-poly already)
  - Normals: Import (or Calculate, 30°)
  - Tangents: Calculate Mikktspace
- **Rig tab**: Animation Type: None
- **Materials tab**: Material Creation Mode: Standard / URP / HDRP Lit (whatever your render pipeline uses)
  - Then drop your real textures onto the auto-created materials. The FBX brings UVs and one material slot per mesh.

## Working with emissive alien models

The emission values above are calibrated for Blender's Cycles/Eevee tone-mapping. In Unity:
- For URP/HDRP: enable Emission on the Lit material, set the color and HDR intensity to taste.
- Crystals, Bioluminescent Frond, and Pulsing Nodule are the strongest emitters — wire them to your scene's bloom post-process for the cleanest look.
- The Spore Pod Cluster and Moss Patch have a faint emission to suggest "alive but not glowy."

## Source file

`Source_UnityAssets.blend` contains every mesh and material grouped in one scene. Re-export individual FBX from there if you tweak something.

## Notes on the sculpted look

Each model was built procedurally using:
- A base primitive (icosphere / cylinder / hex-prism / grid)
- Multi-frequency Perlin noise displacement for organic surface variation
- One level of Catmull-Clark subdivision (applied) for smooth silhouettes
- Smart UV projection with a 66° angle limit (30° on crystals to keep facet seams)
- Auto-smooth shading at 30–60° (no smoothing on crystals — kept faceted)

If you need higher-detail variants later, the source `.blend` re-runs deterministically from the seeds in the helper functions — just bump up the subdivision level or skip the decimate step.
