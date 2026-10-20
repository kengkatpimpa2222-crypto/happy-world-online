using System;
using System.Collections.Generic;
using HappyWorld.Core.Art;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HappyWorld.Unity.Art
{
    /// <summary>
    /// A player as an anime-style 3D human (VRoid Studio sample models, CC0, see ART_CREDITS.md): one skeleton
    /// per body type, with the face on it and outfit, hair and hat parts swapped in and out as skinned meshes.
    ///
    /// The skeleton gets a Unity Humanoid <see cref="UnityEngine.Avatar"/> (built at runtime from the model's VRM
    /// bone map) on an Animator, so humanoid clips and IK can be added later; today the body moves with
    /// <see cref="HumanGait"/> (idle / walk / run by speed), hair and skirts sway with <see cref="SpringBones"/>,
    /// the eyes blink and the face smiles when its player chats. It only reads the transform it sits on, so the
    /// network sync of positions is exactly as before. Returns null from <see cref="Create"/> when the art can't
    /// load: the caller falls back to the older characters.
    /// </summary>
    public sealed class AnimeAvatar : MonoBehaviour
    {
        /// <summary>World units per metre: a VRoid girl (~1.55 m) is a little shorter than the old 1.9-unit figure.</summary>
        public const float UnitsPerMetre = 1.18f;
        /// <summary>Beyond this distance from the camera the sway is paused (it can't be seen on a phone anyway).</summary>
        private const float SpringDistance = 32f;

        private sealed class Slot
        {
            public string Key;
            public AvatarPart Part;
            public GameObject Go;
            public readonly List<Transform> Bones = new List<Transform>();
        }

        private static readonly Dictionary<string, UnityEngine.Avatar> Avatars = new Dictionary<string, UnityEngine.Avatar>();

        public string Body { get; private set; }
        /// <summary>Head bone: accessories and effects can follow it.</summary>
        public Transform Head { get; private set; }

        private Transform _turn, _model;
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        private readonly Dictionary<Transform, Quaternion> _restRot = new Dictionary<Transform, Quaternion>();
        private Transform[] _gaitBones;
        private Quaternion[] _gaitRest;
        private Transform _hips;
        private Vector3 _hipsRest;
        private readonly HumanGait _gait = new HumanGait();
        private SpringBones _springs = new SpringBones();
        private readonly List<SpringBones.Sphere> _colliders = new List<SpringBones.Sphere>();
        private readonly Dictionary<string, int[]> _colliderGroups = new Dictionary<string, int[]>();
        private readonly Slot _outfit = new Slot(), _hair = new Slot(), _hat = new Slot();
        private SkinnedMeshRenderer _face;
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private MaterialPropertyBlock _mpb;
        private Color _skin = Color.white;
        private int _blinkShape = -1, _joyShape = -1;
        private float _nextBlink, _blinkT = -1f, _smileUntil, _smile;
        private Vector3 _lastPos;
        private Quaternion _facing;
        private bool _started;

        public static AnimeAvatar Create(Transform parent, string body)
        {
            var basePart = AvatarPart.Get(AvatarLooks.Base(body));
            if (basePart == null || basePart.Mesh == null) return null;
            var root = new GameObject("AnimeAvatar_" + body);
            root.transform.SetParent(parent, false);
            var a = root.AddComponent<AnimeAvatar>();
            try
            {
                a.Build(basePart, body);
                return a;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HappyWorld] anime avatar failed, using the old character: " + ex);
                Destroy(root);
                return null;
            }
        }

        private void Build(AvatarPart basePart, string body)
        {
            Body = body;
            _mpb = new MaterialPropertyBlock();
            // AnimeAvatar (follows the player) > Turn (smoothed facing, scale) > Model (Animator) > Root > J_Bip_C_Hips ...
            _turn = new GameObject("Turn").transform;
            _turn.SetParent(transform, false);
            _turn.localScale = Vector3.one * UnitsPerMetre;
            _model = new GameObject("Model").transform;
            _model.SetParent(_turn, false);
            var m = basePart.Model;
            foreach (var r in m.Roots) BuildNode(m, r, _model, null);

            var humanoid = basePart.Extras["humanoid"] as JObject;
            if (humanoid == null) throw new Exception("base part has no humanoid map");
            _gaitBones = new Transform[HumanGait.Bones.Length];
            _gaitRest = new Quaternion[HumanGait.Bones.Length];
            for (int i = 0; i < HumanGait.Bones.Length; i++)
            {
                var name = (string)humanoid[HumanGait.Bones[i]];
                Transform t;
                if (name != null && _bones.TryGetValue(name, out t))
                {
                    _gaitBones[i] = t;
                    _gaitRest[i] = t.localRotation;
                }
            }
            _hips = Bone(humanoid, "hips");
            Head = Bone(humanoid, "head");
            if (_hips == null || Head == null) throw new Exception("skeleton has no hips/head");
            _hipsRest = _hips.localPosition;
            _gait.LegLength = Mathf.Max(0.4f, _model.InverseTransformPoint(_hips.position).y);

            var colliders = basePart.Extras["colliders"] as JArray;
            if (colliders != null)
            {
                var groups = new Dictionary<string, List<int>>();
                foreach (JObject c in colliders)
                {
                    Transform t;
                    var bone = (string)c["bone"];
                    var o = (JArray)c["offset"];
                    if (bone == null || !_bones.TryGetValue(bone, out t)) continue;
                    List<int> ids;
                    if (!groups.TryGetValue(bone, out ids)) groups[bone] = ids = new List<int>();
                    ids.Add(_colliders.Count);
                    _colliders.Add(new SpringBones.Sphere { Bone = t, Offset = new Vector3((float)o[0], (float)o[1], (float)o[2]), Radius = (float)c["radius"] });
                }
                foreach (var kv in groups) _colliderGroups[kv.Key] = kv.Value.ToArray();
            }

            _face = Attach(basePart, null);
            _blinkShape = basePart.Mesh.GetBlendShapeIndex("blink");
            _joyShape = basePart.Mesh.GetBlendShapeIndex("joy");
            _nextBlink = Time.time + UnityEngine.Random.Range(1f, 4f);
            BuildHumanoid(humanoid);
        }

        private Transform Bone(JObject humanoid, string human)
        {
            var name = (string)humanoid[human];
            Transform t;
            return name != null && _bones.TryGetValue(name, out t) ? t : null;
        }

        private void BuildNode(GlbModel m, int i, Transform parent, List<Transform> created)
        {
            var n = m.Nodes[i];
            if (n.Mesh >= 0) return; // the mesh node: its SkinnedMeshRenderer is made by Attach
            Transform t;
            if (!_bones.TryGetValue(n.Name, out t))
            {
                t = new GameObject(n.Name).transform;
                t.SetParent(parent, false);
                t.localPosition = new Vector3(n.T[0], n.T[1], n.T[2]);
                t.localRotation = new Quaternion(n.R[0], n.R[1], n.R[2], n.R[3]);
                t.localScale = new Vector3(n.S[0], n.S[1], n.S[2]);
                _bones[n.Name] = t;
                _restRot[t] = t.localRotation;
                if (created != null) created.Add(t);
            }
            foreach (var c in n.Children) BuildNode(m, c, t, created);
        }

        /// <summary>
        /// A Humanoid Avatar from the VRM bone map, built once per body type (every instance has the same bone
        /// names and rest pose). Logged, not fatal, if Unity rejects it: the procedural motion doesn't need it.
        /// </summary>
        private void BuildHumanoid(JObject humanoid)
        {
            UnityEngine.Avatar avatar;
            if (!Avatars.TryGetValue(Body, out avatar))
            {
                var human = new List<HumanBone>();
                foreach (var kv in humanoid)
                {
                    HumanBodyBones hb;
                    var enumName = char.ToUpperInvariant(kv.Key[0]) + kv.Key.Substring(1); // leftUpperLeg -> LeftUpperLeg
                    if (!Enum.TryParse(enumName, out hb) || hb == HumanBodyBones.LastBone) continue;
                    var hbName = HumanTrait.BoneName[(int)hb];
                    human.Add(new HumanBone { humanName = hbName, boneName = (string)kv.Value, limit = new HumanLimit { useDefaultValues = true } });
                }
                var skeleton = new List<SkeletonBone>();
                foreach (var t in _model.GetComponentsInChildren<Transform>())
                    skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
                var desc = new HumanDescription
                {
                    human = human.ToArray(), skeleton = skeleton.ToArray(),
                    upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                    armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
                };
                avatar = AvatarBuilder.BuildHumanAvatar(_model.gameObject, desc);
                avatar.name = "AnimeAvatar_" + Body;
                if (!avatar.isValid || !avatar.isHuman) Debug.LogWarning("[HappyWorld] humanoid avatar for " + Body + " is not valid");
                Avatars[Body] = avatar;
            }
            var animator = _model.gameObject.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;
        }

        /// <summary>A part's mesh on this skeleton (creating the part's own sway bones); null part = nothing.</summary>
        private SkinnedMeshRenderer Attach(AvatarPart part, Slot slot)
        {
            if (part == null || part.Mesh == null) return null;
            var created = slot != null ? slot.Bones : null;
            foreach (var r in part.Model.Roots) BuildNode(part.Model, r, _model, created);
            var go = new GameObject(part.Mesh.name);
            go.transform.SetParent(_model, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            var bones = new Transform[part.BoneNames.Length];
            for (int i = 0; i < bones.Length; i++)
                if (!_bones.TryGetValue(part.BoneNames[i], out bones[i])) throw new Exception("part bone " + part.BoneNames[i] + " missing");
            smr.sharedMesh = part.Mesh;
            smr.bones = bones;
            smr.rootBone = _bones["Root"];
            smr.sharedMaterials = part.Materials;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            smr.updateWhenOffscreen = false;
            // bounds in Root space: the rest mesh grown a little so swinging arms and legs aren't culled early
            var b = part.Mesh.bounds;
            b.Expand(0.5f);
            smr.localBounds = b;
            _renderers.Add(smr);
            ApplySkin(smr);
            if (slot != null) slot.Go = go;
            return smr;
        }

        /// <summary>
        /// Puts on the given parts (keys from <see cref="AvatarLooks"/>); unchanged slots are left alone, a part that
        /// fails to load keeps what was worn before. Skin tone is a multiplier for the skin texture.
        /// </summary>
        public void Dress(string outfitKey, string hairKey, string hatKey, Color skin)
        {
            bool changed = Swap(_outfit, outfitKey) | Swap(_hair, hairKey) | Swap(_hat, hatKey);
            if (changed) RebuildSprings();
            if (skin != _skin || changed)
            {
                _skin = skin;
                foreach (var r in _renderers) if (r != null) ApplySkin(r);
            }
        }

        private bool Swap(Slot slot, string key)
        {
            if (slot.Key == key) return false;
            AvatarPart part = null;
            if (key != null)
            {
                part = AvatarPart.Get(key);
                if (part == null) return false; // keep what is worn
            }
            if (slot.Go != null)
            {
                _renderers.Remove(slot.Go.GetComponent<Renderer>());
                Destroy(slot.Go);
            }
            foreach (var t in slot.Bones)
            {
                _bones.Remove(t.name);
                _restRot.Remove(t);
            }
            foreach (var t in slot.Bones)
                if (t != null && (t.parent == null || !slot.Bones.Contains(t.parent)))
                {
                    t.SetParent(null, false); // out of the skeleton at once (Destroy happens at the end of the frame)
                    Destroy(t.gameObject);
                }
            slot.Bones.Clear();
            slot.Go = null;
            slot.Key = key;
            slot.Part = part;
            if (part != null) Attach(part, slot);
            return true;
        }

        private void RebuildSprings()
        {
            foreach (var kv in _restRot)
                if (kv.Key != null && !Array.Exists(_gaitBones, b => b == kv.Key)) kv.Key.localRotation = kv.Value;
            _springs = new SpringBones();
            foreach (var s in new[] { _outfit, _hair, _hat })
                if (s.Part != null) _springs.Add(s.Part.Extras["springs"] as JArray, _bones, _colliders, _colliderGroups);
        }

        private void ApplySkin(Renderer r)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_SkinTint", _skin);
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>Smiles for a few seconds (shown when the player chats).</summary>
        public void Smile(float seconds = 2.5f) => _smileUntil = Time.time + seconds;

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            var pos = transform.position;
            if (!_started)
            {
                _started = true;
                _lastPos = pos;
                _facing = transform.rotation;
            }
            var scale = Mathf.Max(0.01f, _turn.lossyScale.x);
            var moved = new Vector2(pos.x - _lastPos.x, pos.z - _lastPos.z).magnitude / scale;
            _lastPos = pos;
            if (moved > 3f) // teleported (zone change, home): no sprint, no flying hair
            {
                moved = 0f;
                _springs.Reset();
                _facing = transform.rotation;
            }

            // facing: remote players turn in steps of the network direction; ease the body round instead
            _facing = Quaternion.Slerp(_facing, transform.rotation, 1f - Mathf.Exp(-14f * dt));
            _turn.rotation = _facing;

            _gait.Step(dt, moved);
            var q = _gait.Rotations;
            for (int i = 0; i < _gaitBones.Length; i++)
                if (_gaitBones[i] != null)
                    _gaitBones[i].localRotation = _gaitRest[i] * new Quaternion(q[i * 4], q[i * 4 + 1], q[i * 4 + 2], q[i * 4 + 3]);
            _hips.localPosition = _hipsRest + new Vector3(_gait.HipsX, _gait.HipsY, _gait.HipsZ);

            var cam = Camera.main;
            if (cam == null || (cam.transform.position - pos).sqrMagnitude < SpringDistance * SpringDistance) _springs.Tick(dt);
            else _springs.Reset();

            Face(dt);
        }

        private void Face(float dt)
        {
            if (_face == null) return;
            if (_blinkShape >= 0)
            {
                if (_blinkT < 0f && Time.time >= _nextBlink) _blinkT = 0f;
                float w = 0f;
                if (_blinkT >= 0f)
                {
                    _blinkT += dt;
                    const float close = 0.06f, hold = 0.04f, open = 0.09f;
                    w = _blinkT < close ? _blinkT / close : _blinkT < close + hold ? 1f : 1f - (_blinkT - close - hold) / open;
                    if (_blinkT >= close + hold + open)
                    {
                        _blinkT = -1f;
                        w = 0f;
                        _nextBlink = Time.time + UnityEngine.Random.Range(2.2f, 5.5f);
                    }
                }
                _face.SetBlendShapeWeight(_blinkShape, Mathf.Clamp01(w) * 100f * (1f - _smile * 0.5f));
            }
            if (_joyShape >= 0)
            {
                _smile = Mathf.MoveTowards(_smile, Time.time < _smileUntil ? 1f : 0f, dt * 4f);
                _face.SetBlendShapeWeight(_joyShape, _smile * 100f);
            }
        }
    }
}
