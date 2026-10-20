using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HappyWorld.Unity.Art
{
    /// <summary>
    /// Hair, skirts, ribbons and hoods that sway: the VRM 0.x spring bone (verlet tail per bone, pulled back to
    /// its rest direction by stiffness, gravity and drag, pushed out of sphere colliders on the legs, arms and
    /// head) with the numbers each VRoid sample model shipped with. Stepped by <see cref="AnimeAvatar"/> right
    /// after the body pose, so the sway follows the walk. Far-away avatars skip it (see <see cref="Tick"/>).
    /// </summary>
    public sealed class SpringBones
    {
        private sealed class Joint
        {
            public Transform Node;
            public Quaternion RestLocal;
            public Vector3 BoneAxis; // local direction to the tail
            public float Length;     // local length
            public Vector3 Tail, PrevTail;
            public float Stiffness, Gravity, Drag, Radius;
            public Vector3 GravityDir;
            public Sphere[] Colliders;
        }

        public struct Sphere
        {
            public Transform Bone;
            public Vector3 Offset;
            public float Radius;
        }

        private readonly List<Joint> _joints = new List<Joint>();
        private bool _settled;

        public int Count => _joints.Count;

        /// <summary>Adds the "springs" of a part (its extras) using the body's colliders (base extras).</summary>
        public void Add(JArray springs, IDictionary<string, Transform> bones, IList<Sphere> colliders, IDictionary<string, int[]> colliderGroups)
        {
            if (springs == null) return;
            foreach (JObject g in springs)
            {
                var spheres = new List<Sphere>();
                var groups = g["colliders"] as JArray;
                if (groups != null)
                    foreach (var name in groups)
                    {
                        int[] ids;
                        if (colliderGroups.TryGetValue((string)name, out ids)) foreach (var i in ids) spheres.Add(colliders[i]);
                    }
                var gd = g["gravityDir"] as JArray;
                var gravityDir = gd != null && gd.Count == 3 ? new Vector3((float)gd[0], (float)gd[1], (float)gd[2]) : Vector3.down;
                foreach (var root in (JArray)g["bones"])
                {
                    Transform t;
                    if (!bones.TryGetValue((string)root, out t)) continue;
                    AddChain(t, (float)g["stiffness"], (float)g["gravityPower"], gravityDir, (float)g["drag"], (float)g["hitRadius"], spheres.ToArray());
                }
            }
        }

        private void AddChain(Transform t, float stiffness, float gravity, Vector3 gravityDir, float drag, float radius, Sphere[] spheres)
        {
            Vector3 tailLocal;
            if (t.childCount > 0) tailLocal = t.GetChild(0).localPosition;
            else
            {
                // a chain's last bone: carry on in the direction the chain was going, 7 cm (VRM's rule)
                var d = t.localPosition;
                tailLocal = d.sqrMagnitude > 1e-8f ? d.normalized * 0.07f : Vector3.down * 0.07f;
            }
            if (tailLocal.sqrMagnitude > 1e-8f)
                _joints.Add(new Joint
                {
                    Node = t, RestLocal = t.localRotation, BoneAxis = tailLocal.normalized, Length = tailLocal.magnitude,
                    Stiffness = stiffness, Gravity = gravity, GravityDir = gravityDir, Drag = drag, Radius = radius, Colliders = spheres,
                });
            for (int i = 0; i < t.childCount; i++) AddChain(t.GetChild(i), stiffness, gravity, gravityDir, drag, radius, spheres);
        }

        /// <summary>Forget the motion (after a teleport, a new outfit, or coming back into view).</summary>
        public void Reset() => _settled = false;

        /// <summary>Runs one step; parents are processed before children because chains are added root-first.</summary>
        public void Tick(float dt)
        {
            if (_joints.Count == 0 || dt <= 0f) return;
            dt = Mathf.Min(dt, 1f / 20f);
            if (!_settled)
            {
                foreach (var j in _joints)
                {
                    j.Node.localRotation = j.RestLocal;
                    j.Tail = j.PrevTail = j.Node.TransformPoint(j.BoneAxis * j.Length);
                }
                _settled = true;
                return;
            }
            foreach (var j in _joints)
            {
                var node = j.Node;
                var parentRot = node.parent != null ? node.parent.rotation : Quaternion.identity;
                var restRot = parentRot * j.RestLocal;
                var head = node.position;
                var scale = node.lossyScale.x;
                var length = j.Length * scale;
                var restDir = restRot * j.BoneAxis;
                var next = j.Tail + (j.Tail - j.PrevTail) * (1f - j.Drag)
                           + restDir * (j.Stiffness * dt * scale)
                           + j.GravityDir * (j.Gravity * dt * scale);
                next = head + (next - head).normalized * length;
                foreach (var c in j.Colliders)
                {
                    if (c.Bone == null) continue;
                    var center = c.Bone.TransformPoint(c.Offset);
                    var r = (c.Radius + j.Radius) * scale;
                    var off = next - center;
                    if (off.sqrMagnitude < r * r)
                    {
                        next = center + off.normalized * r;
                        next = head + (next - head).normalized * length;
                    }
                }
                if ((next - j.Tail).sqrMagnitude > length * length * 25f) j.Tail = next = head + restDir * length; // teleported
                j.PrevTail = j.Tail;
                j.Tail = next;
                node.rotation = Quaternion.FromToRotation(restDir, next - head) * restRot;
            }
        }
    }
}
