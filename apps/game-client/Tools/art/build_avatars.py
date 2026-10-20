#!/usr/bin/env python3
"""
Builds the anime player avatars (Resources/Art/Models/avatar) from the CC0 VRoid Studio sample models.

    python3 build_avatars.py <folder with the .vrm files> [out folder]

Needs numpy, Pillow and meshoptimizer (pip install numpy pillow meshoptimizer). The .vrm files come from
https://github.com/madjin/vrm-samples (folder vroid/beta); every file used here carries licenseName "CC0" and
commercialUssageName "Allow" in its own VRM meta, which the script checks before touching it.

What comes out, per body type (male / female):
  <body>.bytes                 skeleton (VRoid J_Bip bones + eye bones) and the face with blink/smile shapes
  <body>_outfit_<id>.bytes     body skin + clothes + shoes of one sample model, refitted to that skeleton
  <body>_hair_<id>.bytes       one sample model's hair, with its own spring (sway) bones
  <body>_hat_straw.bytes       a straw hat made here, rigid on the head
  *.png                        one texture atlas per part (hair keeps its textures: its UVs tile)

Every part is moved onto the body type's own skeleton ("refitted"): each vertex moves by the weighted offset
between the donor model's bones and the target bones, so an outfit from a taller model fits a shorter one.
The VRoid rest pose is a T-pose with every bone rotation identity, which is what makes that a pure translation.
Everything is turned 180 degrees so the character faces +Z: after the glTF->Unity X flip the result matches
UniVRM's coordinates, so the VRM 0.x spring/collider numbers (stored in Unity space) can be used as they are.
"""
import json
import math
import os
import struct
import sys

import meshoptimizer
import numpy as np
from PIL import Image

# ---------------------------------------------------------------- the line-up

BODIES = {
    "female": {
        "base": "Sendagaya_Shino",
        "outfits": {
            "casual": ("Vivi", None),
            "basic_tee": ("HairSample_Female", None),
            "school_uniform": ("Sendagaya_Shino", None),
            "thai_silk": ("Darkness_Shibu", None),
            "star_idol": ("Victoria_Rubin", None),
        },
        "hairs": {"bob_black": "Sendagaya_Shibu", "long_black": "Sendagaya_Shino", "short_brown": "Vivi"},
    },
    "male": {
        "base": "Sakurada_Fumiriya",
        "outfits": {
            "casual": ("HairSample_Male", None),
            "school_uniform": ("Sakurada_Fumiriya", None),
            # only two male sample outfits exist: these two are recoloured copies (see recolor())
            "thai_silk": ("Sakurada_Fumiriya", {"Tops": ("hue", 0.83, 0.65, 1.5)}),
            "star_idol": ("HairSample_Male", {"Tops": ("tint", (1.0, 0.8, 0.32))}),
        },
        "hairs": {"short_brown": "Sakurada_Fumiriya", "messy_black": "HairSample_Male"},
    },
}

# share of triangles kept per part (mobile budget ~10k triangles per character); the face keeps everything
KEEP = {"skin": 0.5, "outfit": 0.7, "hair": 0.3}
ATLAS = 1024
FACE_CELL = {"Face_00_SKIN": 512}
BODY_CELL = {"Body_00_SKIN": 512, "Tops_01": 512, "Onepiece_01": 512, "Bottoms_01": 512}
CANON_PREFIX = ("J_Bip_", "J_Adj_")


# ---------------------------------------------------------------- VRM reading

class Vrm:
    def __init__(self, path):
        data = open(path, "rb").read()
        assert data[:4] == b"glTF", path
        jlen = struct.unpack("<I", data[12:16])[0]
        self.j = json.loads(data[20:20 + jlen])
        blen = struct.unpack("<I", data[20 + jlen:24 + jlen])[0]
        self.bin = data[28 + jlen:28 + jlen + blen]
        self.name = os.path.splitext(os.path.basename(path))[0]
        self.vrm = self.j["extensions"]["VRM"]
        meta = self.vrm["meta"]
        if meta.get("licenseName") != "CC0" or meta.get("commercialUssageName") != "Allow":
            raise SystemExit(f"{self.name}: not CC0 / commercial use not allowed, refusing to use it")
        self.nodes = self.j["nodes"]
        self.parent = {}
        for i, n in enumerate(self.nodes):
            for c in n.get("children", []):
                self.parent[c] = i
            if n.get("rotation", [0, 0, 0, 1]) != [0, 0, 0, 1] or n.get("scale", [1, 1, 1]) != [1, 1, 1]:
                raise SystemExit(f"{self.name}: node {n['name']} is not a pure translation (not a VRoid T-pose)")
        self.world = np.zeros((len(self.nodes), 3))
        for i in range(len(self.nodes)):
            p, k = np.zeros(3), i
            while k is not None:
                p += self.nodes[k].get("translation", [0, 0, 0])
                k = self.parent.get(k)
            self.world[i] = p
        self.by_name = {n["name"]: i for i, n in enumerate(self.nodes)}

    def acc(self, i):
        a = self.j["accessors"][i]
        bv = self.j["bufferViews"][a["bufferView"]]
        dt = {5126: np.float32, 5123: np.uint16, 5125: np.uint32, 5121: np.uint8}[a["componentType"]]
        w = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}[a["type"]]
        off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        assert bv.get("byteStride", 0) in (0, w * np.dtype(dt).itemsize)
        return np.frombuffer(self.bin, dt, a["count"] * w, off).reshape(a["count"], w).copy()

    def image(self, material_name):
        """(RGBA image, shade colour) of a material, with the MToon colour factor baked in."""
        mp = next(m for m in self.vrm["materialProperties"] if m["name"] == material_name)
        tex = self.j["textures"][mp["textureProperties"]["_MainTex"]]
        bv = self.j["bufferViews"][self.j["images"][tex["source"]]["bufferView"]]
        raw = self.bin[bv.get("byteOffset", 0):bv.get("byteOffset", 0) + bv["byteLength"]]
        import io
        img = np.asarray(Image.open(io.BytesIO(raw)).convert("RGBA")).astype(np.float32)
        col = mp["vectorProperties"].get("_Color", [1, 1, 1, 1])
        img[..., :4] *= np.array(col, np.float32)
        shade = mp["vectorProperties"].get("_ShadeColor", [0.8, 0.8, 0.8, 1])[:3]
        return Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)), shade

    def hair_material(self, m):
        return "_HAIR" in m


class Part:
    """Triangles of one or more materials, with per-vertex data and bone names."""

    def __init__(self):
        self.groups = []  # (material name, pos, nrm, uv, joints[n,4] of names idx, weights, morphs{name: delta})
        self.bones = []   # bone names referenced by joints


def extract(vrm, mesh_name, keep, morphs=()):
    """Every primitive of mesh `mesh_name` whose material passes keep(name), split per material."""
    mi = next(i for i, m in enumerate(vrm.j["meshes"]) if m["name"] == mesh_name)
    node = next(n for n in vrm.nodes if n.get("mesh") == mi)
    joints = vrm.j["skins"][node["skin"]]["joints"]
    mesh = vrm.j["meshes"][mi]
    morph_index = {}
    for g in vrm.vrm["blendShapeMaster"]["blendShapeGroups"]:
        if g["presetName"] in morphs and g["binds"]:
            morph_index[g["presetName"]] = g["binds"][0]["index"]
    per_mat = {}
    for pr in mesh["primitives"]:
        mat = vrm.j["materials"][pr["material"]]["name"]
        if not keep(mat):
            continue
        per_mat.setdefault(mat, []).append(pr)
    part = Part()
    bone_names = {}
    for mat, prims in per_mat.items():
        a = prims[0]["attributes"]
        pos, nrm, uv = vrm.acc(a["POSITION"]), vrm.acc(a["NORMAL"]), vrm.acc(a["TEXCOORD_0"])
        jn, wt = vrm.acc(a["JOINTS_0"]).astype(np.int64), vrm.acc(a["WEIGHTS_0"]).astype(np.float64)
        tris = np.concatenate([vrm.acc(p["indices"]).ravel() for p in prims]).astype(np.int64)
        for p in prims:
            assert p["attributes"]["POSITION"] == a["POSITION"], "primitives of one material must share vertices"
        mdeltas = {k: vrm.acc(prims[0]["targets"][ix]["POSITION"]) for k, ix in morph_index.items()}
        used, remap = np.unique(tris, return_inverse=True)
        names = np.zeros((len(used), 4), np.int64)
        for r, vi in enumerate(used):
            for k in range(4):
                nm = vrm.nodes[joints[jn[vi, k]]]["name"]
                if nm not in bone_names:
                    bone_names[nm] = len(part.bones)
                    part.bones.append(nm)
                names[r, k] = bone_names[nm]
        w = wt[used]
        w /= np.maximum(w.sum(1, keepdims=True), 1e-9)
        part.groups.append(dict(mat=mat, pos=pos[used].astype(np.float64), nrm=nrm[used], uv=uv[used], joints=names,
                                weights=w, tris=remap.reshape(-1, 3), morphs={k: d[used] for k, d in mdeltas.items()}))
    return part


# ---------------------------------------------------------------- skeleton, refit

class Skeleton:
    """The body type's bones: name -> (parent name, world position), in source (glTF, -Z facing) space."""

    def __init__(self, vrm):
        self.order, self.parent, self.world = [], {}, {}
        for i, n in enumerate(vrm.nodes):
            if n["name"] == "Root" or n["name"].startswith(CANON_PREFIX):
                self.add(n["name"], vrm.nodes[vrm.parent[i]]["name"] if i in vrm.parent else None, vrm.world[i])
        for nm in list(self.order):  # make sure every ancestor is in (e.g. J_Adj_L_FaceEyeSet)
            assert self.parent[nm] is None or self.parent[nm] in self.world, nm

    def add(self, name, parent, world):
        if name in self.world:
            return
        self.order.append(name)
        self.parent[name] = parent
        self.world[name] = np.array(world, np.float64)


def canonical_ancestor(vrm, skel, name):
    i = vrm.by_name[name]
    while vrm.nodes[i]["name"] not in skel.world:
        i = vrm.parent[i]
    return vrm.nodes[i]["name"]


def refit(part, vrm, skel, extra_roots=()):
    """Moves part onto skel; returns the secondary bones (name, parent, world) the part needs in skel space."""
    delta = {}
    extra = []

    def d(name):
        if name not in delta:
            anc = canonical_ancestor(vrm, skel, name)
            delta[name] = skel.world[anc] - vrm.world[vrm.by_name[anc]]
        return delta[name]

    need = set(b for b in part.bones if b not in skel.world)
    for r in extra_roots:  # spring chains: the whole chain, including unskinned tips (*_end)
        stack = [vrm.by_name[r]]
        while stack:
            k = stack.pop()
            need.add(vrm.nodes[k]["name"])
            stack.extend(vrm.nodes[k].get("children", []))
    for b in list(need):  # and their non-canonical ancestors
        k = vrm.by_name[b]
        while vrm.nodes[k]["name"] not in skel.world:
            need.add(vrm.nodes[k]["name"])
            k = vrm.parent[k]
    for i, n in enumerate(vrm.nodes):  # file order keeps parents before children
        if n["name"] in need:
            extra.append((n["name"], vrm.nodes[vrm.parent[i]]["name"], vrm.world[i] + d(n["name"])))
    bone_delta = np.array([d(b) for b in part.bones])
    for g in part.groups:
        g["pos"] = g["pos"] + np.einsum("vk,vkc->vc", g["weights"], bone_delta[g["joints"]])
    return extra


def _hits(orig, dirs, tri, max_dist):
    """Per ray: does it hit any triangle (n,3,3) within max_dist? Moller-Trumbore, vectorised in chunks."""
    hit = np.zeros(len(orig), bool)
    e1, e2, p0 = tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0], tri[:, 0]
    for s in range(0, len(orig), 256):
        o, d = orig[s:s + 256, None, :], dirs[s:s + 256, None, :]
        pv = np.cross(d, e2[None])
        det = np.einsum("rtc,tc->rt", pv, e1)
        inv = 1.0 / np.where(np.abs(det) < 1e-12, 1e-12, det)
        tv = o - p0[None]
        u = np.einsum("rtc,rtc->rt", tv, pv) * inv
        qv = np.cross(tv, e1[None])
        v = np.einsum("rtc,rtc->rt", d, qv) * inv
        t = np.einsum("tc,rtc->rt", e2, qv) * inv
        ok = (u >= 0) & (v >= 0) & (u + v <= 1) & (t > 0) & (t < max_dist)
        hit[s:s + 256] = ok.any(1)
    return hit


def cull_hidden_skin(part, max_dist=0.06):
    """Drops body-skin triangles that clothes cover completely: fewer triangles, and no skin poking through
    the clothes once the mesh is simplified or animated. A vertex is covered when the ray along its normal
    meets clothing within max_dist, and so do most rays a little off it (skin at sleeve edges stays)."""
    cloth = [g for g in part.groups if "_SKIN" not in g["mat"]]
    if not cloth:
        return
    tri = np.concatenate([g["pos"][g["tris"]] for g in cloth])
    for g in part.groups:
        if "_SKIN" not in g["mat"]:
            continue
        n = g["nrm"] / np.maximum(np.linalg.norm(g["nrm"], axis=1, keepdims=True), 1e-9)
        o = g["pos"] + n * 0.002
        votes = _hits(o, n, tri, max_dist).astype(int)
        for tilt in (np.array([0, 0.5, 0]), np.array([0, -0.5, 0]), np.array([0.5, 0, 0]), np.array([-0.5, 0, 0])):
            d = n + tilt
            votes += _hits(o, d / np.linalg.norm(d, axis=1, keepdims=True), tri, max_dist * 1.5)
        covered = votes >= 4
        keep = ~covered[g["tris"]].all(1)
        g["tris"] = g["tris"][keep]


def simplify(part, ratio, lock_border=True, skin_ratio=None):
    """Hair is made of thin cards that are all border: it has to be allowed to collapse them. Clothes are only
    thinned lightly: removing their inner layers (pocket linings) would show the skin underneath."""
    for g in part.groups:
        r = skin_ratio if skin_ratio is not None and "_SKIN" in g["mat"] else ratio
        n = len(g["tris"])
        if n < 300:
            continue
        idx = g["tris"].astype(np.uint32).ravel()
        out = np.zeros_like(idx)
        pos = np.ascontiguousarray(g["pos"], np.float32)
        count = meshoptimizer.simplify(out, idx, pos, target_index_count=int(len(idx) * r) // 3 * 3,
                                       target_error=0.004 if lock_border else 0.03,
                                       options=meshoptimizer.SIMPLIFY_LOCK_BORDER if lock_border else 0)
        tris = out[:count].reshape(-1, 3).astype(np.int64)
        used, remap = np.unique(tris, return_inverse=True)
        for k in ("pos", "nrm", "uv", "joints", "weights"):
            g[k] = g[k][used]
        g["morphs"] = {k: v[used] for k, v in g["morphs"].items()}
        g["tris"] = remap.reshape(-1, 3)


# ---------------------------------------------------------------- textures

def recolor(img, how):
    a = np.asarray(img).astype(np.float32) / 255
    rgb, alpha = a[..., :3], a[..., 3:]
    if how[0] == "tint":
        lum = rgb.mean(-1, keepdims=True)
        rgb = np.clip(lum * np.array(how[1]) * 1.15, 0, 1)
    else:
        _, hue, sat, gain = how
        hsv = np.asarray(Image.fromarray((rgb * 255).astype(np.uint8)).convert("HSV")).astype(np.float32) / 255
        dyed = hsv[..., 2] < 0.5  # the dark vest; the light shirt and sleeve trims stay
        dyed &= ~((hsv[..., 0] < 0.12) & (hsv[..., 1] > 0.35))  # keep the orange tie
        hsv[..., 0] = np.where(dyed, hue, hsv[..., 0])
        hsv[..., 1] = np.where(dyed, np.maximum(hsv[..., 1], sat), hsv[..., 1])
        hsv[..., 2] = np.where(dyed, np.clip(hsv[..., 2] * gain, 0, 1), hsv[..., 2])
        rgb = np.asarray(Image.fromarray((hsv * 255).astype(np.uint8), "HSV").convert("RGB")).astype(np.float32) / 255
    return Image.fromarray((np.concatenate([rgb, alpha], -1) * 255).astype(np.uint8), "RGBA")


def cell_size(mat, table, default=256):
    for k, v in table.items():
        if k in mat:
            return v
    return default


def atlas(vrm, part, out_png, cells, colors=None):
    """Packs every group's texture into one ATLAS x ATLAS image and remaps the UVs into it."""
    items = sorted(part.groups, key=lambda g: -cell_size(g["mat"], cells))
    sheet = Image.new("RGBA", (ATLAS, ATLAS), (0, 0, 0, 0))
    x = y = row = 0
    shades = {}
    for g in items:
        s = cell_size(g["mat"], cells)
        if x + s > ATLAS:
            x, y, row = 0, y + row, 0
        assert y + s <= ATLAS, "atlas full: " + out_png
        img, shade = vrm.image(g["mat"])
        for key, how in (colors or {}).items():
            if key in g["mat"]:
                img = recolor(img, how)
        sheet.paste(img.resize((s, s), Image.LANCZOS), (x, y))
        pad = 1.5
        uv = np.clip(g["uv"], 0, 1)
        g["uv"] = np.stack([(x + pad + uv[:, 0] * (s - 2 * pad)) / ATLAS, (y + pad + uv[:, 1] * (s - 2 * pad)) / ATLAS], 1)
        g["image"] = os.path.basename(out_png)
        shades[g["mat"]] = shade
        x, row = x + s, max(row, s)
    sheet.save(out_png, optimize=True)
    return shades


def own_textures(vrm, part, prefix, size=512):
    shades = {}
    for i, g in enumerate(part.groups):
        img, shade = vrm.image(g["mat"])
        name = f"{prefix}_{i}.png"
        img.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, name), optimize=True)
        g["image"] = name
        shades[g["mat"]] = shade
    return shades


# ---------------------------------------------------------------- springs

def springs_for(vrm, roots_filter):
    """VRM 0.x spring groups whose bones pass the filter, with collider groups named by their bone."""
    sa = vrm.vrm["secondaryAnimation"]
    groups, roots = [], []
    for g in sa["boneGroups"]:
        bones = [vrm.nodes[b]["name"] for b in g["bones"] if roots_filter(vrm.nodes[b]["name"])]
        if not bones:
            continue
        roots += bones
        groups.append({
            "bones": bones,
            "stiffness": g["stiffiness"],
            "gravityPower": g["gravityPower"],
            "gravityDir": [g["gravityDir"]["x"], g["gravityDir"]["y"], g["gravityDir"]["z"]],
            "drag": g["dragForce"],
            "hitRadius": g["hitRadius"],
            "colliders": [vrm.nodes[sa["colliderGroups"][c]["node"]]["name"] for c in g["colliderGroups"]],
        })
    return groups, roots


def colliders_of(vrm):
    out = []
    for cg in vrm.vrm["secondaryAnimation"]["colliderGroups"]:
        for c in cg["colliders"]:
            o = c["offset"]
            out.append({"bone": vrm.nodes[cg["node"]]["name"], "offset": [o["x"], o["y"], o["z"]], "radius": c["radius"]})
    return out


# ---------------------------------------------------------------- GLB writing

def yaw(v):
    v = np.array(v, np.float64)
    v[..., 0] *= -1
    v[..., 2] *= -1
    return v


class Writer:
    def __init__(self):
        self.bin = bytearray()
        self.j = {"asset": {"version": "2.0", "generator": "happy-world build_avatars.py"}, "buffers": [], "bufferViews": [],
                  "accessors": [], "nodes": [], "meshes": [], "skins": [], "materials": [], "images": [], "textures": [],
                  "samplers": [{"magFilter": 9729, "minFilter": 9987}], "scenes": [{"nodes": []}], "scene": 0,
                  "extensionsUsed": ["KHR_materials_unlit"]}

    def accessor(self, arr, comp, typ, target=None, minmax=False):
        arr = np.ascontiguousarray(arr)
        while len(self.bin) % 4:
            self.bin.append(0)
        bv = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": arr.nbytes}
        if target:
            bv["target"] = target
        self.bin += arr.tobytes()
        self.j["bufferViews"].append(bv)
        a = {"bufferView": len(self.j["bufferViews"]) - 1, "componentType": comp, "count": len(arr), "type": typ}
        if minmax:
            a["min"] = arr.min(0).tolist()
            a["max"] = arr.max(0).tolist()
        self.j["accessors"].append(a)
        return len(self.j["accessors"]) - 1

    def material(self, name, image, shade, skin):
        if image not in [i["uri"] for i in self.j["images"]]:
            self.j["images"].append({"uri": image})
            self.j["textures"].append({"source": len(self.j["images"]) - 1, "sampler": 0})
        tex = [i["uri"] for i in self.j["images"]].index(image)
        self.j["materials"].append({
            "name": name, "alphaMode": "MASK", "alphaCutoff": 0.5, "doubleSided": True,
            "pbrMetallicRoughness": {"baseColorTexture": {"index": tex}, "metallicFactor": 0, "roughnessFactor": 1},
            "extensions": {"KHR_materials_unlit": {}},
            "extras": {"shade": [round(float(c), 3) for c in shade], "skin": skin},
        })
        return len(self.j["materials"]) - 1

    def save(self, path):
        while len(self.bin) % 4:
            self.bin.append(0)
        self.j["buffers"] = [{"byteLength": len(self.bin)}]
        for k in ("skins", "images", "textures", "materials", "meshes"):
            if not self.j[k]:
                del self.j[k]
        js = json.dumps(self.j, separators=(",", ":")).encode()
        js += b" " * (-len(js) % 4)
        with open(path, "wb") as f:
            f.write(struct.pack("<III", 0x46546C67, 2, 28 + len(js) + len(self.bin)))
            f.write(struct.pack("<II", len(js), 0x4E4F534A) + js)
            f.write(struct.pack("<II", len(self.bin), 0x004E4942) + bytes(self.bin))


def write_part(path, skel, extra, part, shades, mesh_name, extras):
    w = Writer()
    names = list(skel.order) + [e[0] for e in extra]
    parent = dict(skel.parent)
    world = dict(skel.world)
    for nm, par, wp in extra:
        parent[nm] = par
        world[nm] = wp
    index = {nm: i for i, nm in enumerate(names)}
    for nm in names:
        local = yaw(world[nm]) - (yaw(world[parent[nm]]) if parent[nm] else 0)
        w.j["nodes"].append({"name": nm, "translation": [round(float(c), 6) for c in local]})
    for nm in names:
        if parent[nm]:
            w.j["nodes"][index[parent[nm]]].setdefault("children", []).append(index[nm])
    w.j["scenes"][0]["nodes"].append(index["Root"])
    if part is not None:
        joint_nodes = [index[b] for b in part.bones]
        ibm = np.zeros((len(part.bones), 16), np.float32)
        for k, b in enumerate(part.bones):
            m = np.eye(4)
            m[:3, 3] = -yaw(world[b])
            ibm[k] = m.T.ravel()  # column-major
        w.j["skins"].append({"joints": joint_nodes, "inverseBindMatrices": w.accessor(ibm, 5126, "MAT4"), "skeleton": index["Root"]})
        prims = []
        target_names = sorted({k for g in part.groups for k in g["morphs"]})
        for g in part.groups:
            skin = 1.0 if "_SKIN" in g["mat"] else 0.0
            p = {"attributes": {
                "POSITION": w.accessor(yaw(g["pos"]).astype(np.float32), 5126, "VEC3", 34962, True),
                "NORMAL": w.accessor(yaw(g["nrm"]).astype(np.float32), 5126, "VEC3", 34962),
                "TEXCOORD_0": w.accessor(g["uv"].astype(np.float32), 5126, "VEC2", 34962),
                "JOINTS_0": w.accessor(g["joints"].astype(np.uint16), 5123, "VEC4", 34962),
                "WEIGHTS_0": w.accessor(g["weights"].astype(np.float32), 5126, "VEC4", 34962),
            }, "indices": w.accessor(g["tris"].astype(np.uint16 if len(g["pos"]) < 65536 else np.uint32).ravel(),
                                     5123 if len(g["pos"]) < 65536 else 5125, "SCALAR", 34963),
                "material": w.material(g["mat"], g["image"], shades[g["mat"]], skin)}
            if target_names:
                p["targets"] = [{"POSITION": w.accessor(yaw(g["morphs"][t]).astype(np.float32), 5126, "VEC3", 34962, True)}
                                for t in target_names]
            prims.append(p)
        mesh = {"name": mesh_name, "primitives": prims}
        if target_names:
            mesh["extras"] = {"targetNames": target_names}
        w.j["meshes"].append(mesh)
        w.j["nodes"].append({"name": mesh_name, "mesh": 0, "skin": 0})
        w.j["scenes"][0]["nodes"].append(len(w.j["nodes"]) - 1)
    w.j["extras"] = extras
    w.save(path)
    tris = sum(len(g["tris"]) for g in part.groups) if part else 0
    print(f"  {os.path.basename(path):34s} {tris:6d} tris  {len(names):3d} bones  {os.path.getsize(path) // 1024:5d} KB")
    return tris


# ---------------------------------------------------------------- hat

def straw_hat(skel, top_y):
    """A lathe-turned straw hat with a red band, sitting on the head top."""
    prof = [(0.0, 0.135), (0.07, 0.133), (0.105, 0.115), (0.118, 0.07), (0.122, 0.035), (0.125, 0.02),
            (0.2, 0.012), (0.255, 0.0), (0.262, -0.008)]
    seg = 28
    head = skel.world["J_Bip_C_Head"]
    pos, uv = [], []
    for i in range(seg + 1):
        a = 2 * math.pi * i / seg
        for k, (r, h) in enumerate(prof):
            pos.append([head[0] + r * math.cos(a), top_y + h - 0.06, head[2] + r * math.sin(a) * 1.08])
            uv.append([i / seg, k / (len(prof) - 1)])
    tris = []
    n = len(prof)
    for i in range(seg):
        for k in range(n - 1):
            a, b, c, d = i * n + k, (i + 1) * n + k, (i + 1) * n + k + 1, i * n + k + 1
            tris += [[a, d, b], [b, d, c]]
    pos = np.array(pos)
    nrm = pos - np.array([head[0], top_y - 0.1, head[2]])
    nrm /= np.linalg.norm(nrm, axis=1, keepdims=True)
    tex = np.zeros((128, 128, 4), np.uint8)
    yy, xx = np.mgrid[0:128, 0:128]
    weave = (((xx // 4) + (yy // 4)) % 2) * 18
    tex[..., 0], tex[..., 1], tex[..., 2], tex[..., 3] = 228 - weave, 196 - weave, 118 - weave, 255
    band = (yy >= 34) & (yy < 50)  # profile rows 3-4: the band around the crown
    tex[band] = [200, 52, 60, 255]
    part = Part()
    part.bones = ["J_Bip_C_Head"]
    part.groups.append(dict(mat="StrawHat", pos=pos, nrm=nrm, uv=np.array(uv), tris=np.array(tris),
                            joints=np.zeros((len(pos), 4), np.int64), weights=np.tile([1.0, 0, 0, 0], (len(pos), 1)),
                            morphs={}, image="hat_straw.png"))
    Image.fromarray(tex, "RGBA").save(os.path.join(OUT, "hat_straw.png"))
    return part, {"StrawHat": (0.75, 0.62, 0.45)}


# ---------------------------------------------------------------- main

def main(src):
    os.makedirs(OUT, exist_ok=True)
    cache = {}

    def load(name):
        if name not in cache:
            cache[name] = Vrm(os.path.join(src, name + ".vrm"))
        return cache[name]

    manifest = {}
    for body, spec in BODIES.items():
        print(body)
        base = load(spec["base"])
        skel = Skeleton(base)
        face = extract(base, "Face.baked", lambda m: True, morphs=("blink", "joy"))
        shades = atlas(base, face, os.path.join(OUT, f"{body}_face.png"), FACE_CELL)
        humanoid = {h["bone"]: base.nodes[h["node"]]["name"] for h in base.vrm["humanoid"]["humanBones"]}
        top = max(g["pos"][:, 1].max() for g in face.groups)
        totals = {"face": write_part(os.path.join(OUT, f"{body}.bytes"), skel, refit(face, base, skel), face, shades, "Face", {
            "humanoid": humanoid,
            "colliders": colliders_of(base),
            "license": "CC0 (VRoid Studio sample model " + base.name + ", pixiv Inc.)",
        })}
        entry = {"outfits": {}, "hairs": {}}
        crown_top = 0.0  # the hat sits on the fullest hair of this body type
        for oid, (donor, colors) in spec["outfits"].items():
            v = load(donor)
            part = extract(v, "Body.baked", lambda m: not v.hair_material(m))
            springs, roots = springs_for(v, lambda nm: not nm.startswith("HairJoint") and "Hair" not in nm)
            extra = refit(part, v, skel, roots)
            cull_hidden_skin(part)
            simplify(part, KEEP["outfit"], skin_ratio=KEEP["skin"])
            sh = atlas(v, part, os.path.join(OUT, f"{body}_outfit_{oid}.png"), BODY_CELL, colors)
            totals["outfit_" + oid] = write_part(os.path.join(OUT, f"{body}_outfit_{oid}.bytes"), skel, extra, part, sh, "Outfit",
                                                 {"springs": springs, "license": "CC0 (VRoid Studio sample model " + v.name + ")"})
            entry["outfits"][oid] = v.name + (" (recoloured)" if colors else "")
        for hid, donor in spec["hairs"].items():
            v = load(donor)
            part = extract(v, "Hair001.baked", lambda m: True)
            back = extract(v, "Body.baked", v.hair_material)  # VRoid keeps the back hair in the body mesh
            for g in back.groups:
                g["joints"] = np.vectorize(lambda k: _merge_bone(part, back.bones[k]))(g["joints"])
            part.groups += back.groups
            springs, roots = springs_for(v, lambda nm: nm.startswith("HairJoint") or "Hair" in nm)
            extra = refit(part, v, skel, roots)
            simplify(part, KEEP["hair"], lock_border=False)
            sh = own_textures(v, part, f"{body}_hair_{hid}")
            totals["hair_" + hid] = write_part(os.path.join(OUT, f"{body}_hair_{hid}.bytes"), skel, extra, part, sh, "Hair",
                                               {"springs": springs, "license": "CC0 (VRoid Studio sample model " + v.name + ")"})
            entry["hairs"][hid] = v.name
            head = skel.world["J_Bip_C_Head"]
            for g in part.groups:
                near = (np.abs(g["pos"][:, 0] - head[0]) < 0.05) & (np.abs(g["pos"][:, 2] - head[2]) < 0.05)
                if near.any():
                    crown_top = max(crown_top, float(g["pos"][near, 1].max()))
        hat, sh = straw_hat(skel, crown_top + 0.005 if crown_top else top)
        write_part(os.path.join(OUT, f"{body}_hat_straw.bytes"), skel, [], hat, sh, "Hat", {"license": "made for this project"})
        manifest[body] = entry
        print("  heaviest look:", totals["face"] + max(v for k, v in totals.items() if k.startswith("outfit")) +
              max(v for k, v in totals.items() if k.startswith("hair")), "tris")
    json.dump(manifest, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "avatar_sources.json"), "w"), indent=2)


def _merge_bone(part, name):
    if name not in part.bones:
        part.bones.append(name)
    return part.bones.index(name)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    here = os.path.dirname(os.path.abspath(__file__))
    OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(here, "..", "..", "Assets", "_Project", "Resources", "Art", "Models", "avatar")
    main(sys.argv[1])
