using System;

namespace HappyWorld.Core.Art
{
    /// <summary>
    /// Idle, walk and run for a humanoid in a T-pose rest (the VRoid avatars), computed from code rather than
    /// played from clips: no animation asset to license or import, and the same numbers drive the game and the
    /// preview renders made outside Unity. Every frame <see cref="Step"/> blends idle -> walk -> run by speed and
    /// writes one local rotation per bone in <see cref="Bones"/> (relative to the rest pose) plus a hips offset.
    ///
    /// Space: Unity character space, +Z forward, +Y up, +X the character's right (its left arm points to -X in
    /// the T-pose). Limb angles are "forward pitch" in degrees: positive swings the limb's tip forward.
    /// Feet don't slide much because the cycle advances by distance travelled, not by time.
    /// </summary>
    public sealed class HumanGait
    {
        /// <summary>VRM humanoid bone names, the keys of the avatar's "humanoid" map.</summary>
        public static readonly string[] Bones =
        {
            "hips", "spine", "chest", "upperChest", "neck", "head",
            "leftShoulder", "leftUpperArm", "leftLowerArm", "leftHand",
            "rightShoulder", "rightUpperArm", "rightLowerArm", "rightHand",
            "leftUpperLeg", "leftLowerLeg", "leftFoot", "rightUpperLeg", "rightLowerLeg", "rightFoot",
            "leftIndexProximal", "leftIndexIntermediate", "leftIndexDistal",
            "leftMiddleProximal", "leftMiddleIntermediate", "leftMiddleDistal",
            "leftRingProximal", "leftRingIntermediate", "leftRingDistal",
            "leftLittleProximal", "leftLittleIntermediate", "leftLittleDistal",
            "rightIndexProximal", "rightIndexIntermediate", "rightIndexDistal",
            "rightMiddleProximal", "rightMiddleIntermediate", "rightMiddleDistal",
            "rightRingProximal", "rightRingIntermediate", "rightRingDistal",
            "rightLittleProximal", "rightLittleIntermediate", "rightLittleDistal",
            "leftThumbProximal", "leftThumbIntermediate", "rightThumbProximal", "rightThumbIntermediate",
        };

        /// <summary>Local rotation per bone of <see cref="Bones"/>, xyzw, multiplied onto the rest rotation.</summary>
        public readonly float[] Rotations = new float[Bones.Length * 4];
        /// <summary>Hips offset from rest in metres (character space): the bob and sway.</summary>
        public float HipsX, HipsY, HipsZ;
        /// <summary>Gait cycle 0..1 (left foot forward at 0.25).</summary>
        public float Phase;
        /// <summary>Hips height in the rest pose (metres): scales stride and bob to the body.</summary>
        public float LegLength = 0.8f;
        /// <summary>Smoothed ground speed (m/s), and the 0..1 weights derived from it.</summary>
        public float Speed { get; private set; }
        public float MoveWeight { get; private set; }
        public float RunWeight { get; private set; }

        private float _time;

        public HumanGait()
        {
            for (int i = 0; i < Bones.Length; i++) Rotations[i * 4 + 3] = 1f;
        }

        public static int IndexOf(string bone) => Array.IndexOf(Bones, bone);

        /// <summary>
        /// Advances by <paramref name="dt"/> seconds in which the character moved <paramref name="distance"/> metres
        /// (its own scale: world distance / model scale) and fills <see cref="Rotations"/>.
        /// </summary>
        public void Step(float dt, float distance)
        {
            if (dt <= 0f) return;
            _time += dt;
            var raw = distance / dt;
            // eases in fast (a step starts at once) and out a little slower (no flicker between network updates)
            var rate = raw > Speed ? 10f : 5f;
            Speed += (raw - Speed) * Math.Min(1f, rate * dt);
            var legScale = LegLength / 0.8f;
            MoveWeight = Smooth(0.08f * legScale, 0.6f * legScale, Speed);
            RunWeight = Smooth(1.9f * legScale, 3.6f * legScale, Speed);

            // stride (one full cycle = two steps) grows with speed; cadence is capped so a sprint doesn't flail
            var stride = Lerp(1.25f, 2.3f, RunWeight) * legScale;
            var cadence = Math.Min(Speed / Math.Max(stride, 0.01f), 1.75f);
            if (MoveWeight > 0.001f) Phase = (Phase + cadence * dt) % 1f;
            Pose();
        }

        private void Pose()
        {
            float m = MoveWeight, r = RunWeight, idle = 1f - m;
            float s = Phase * 2f * (float)Math.PI;
            float t = _time;
            float breathe = (float)Math.Sin(t * 2f * Math.PI / 3.6f);
            float shift = (float)Math.Sin(t * 0.55f);

            // ---- legs: thigh swing, knee flex peaking mid-swing, foot kept roughly flat
            for (int side = 0; side < 2; side++)
            {
                float phi = side == 0 ? s : s + (float)Math.PI;
                float sin = (float)Math.Sin(phi), cos = (float)Math.Cos(phi);
                float swing = Math.Max(0f, cos);
                float thigh = m * Lerp(24f * sin, 38f * sin + 14f, r);
                float knee = m * Lerp(4f + 52f * (float)Math.Pow(swing, 1.4), 18f + 92f * (float)Math.Pow(swing, 1.2), r)
                             + idle * (side == 0 ? 2f + 3f * Math.Max(0f, shift) : 2f + 3f * Math.Max(0f, -shift));
                float foot = 0.75f * (thigh - knee) - m * 12f * swing;
                string prefix = side == 0 ? "left" : "right";
                Set(prefix + "UpperLeg", Rot.AroundX(-thigh));
                Set(prefix + "LowerLeg", Rot.AroundX(knee));
                Set(prefix + "Foot", Rot.AroundX(foot));
            }

            // ---- hips and spine: bob, twist with the legs, counter-twist the chest, lean into a run
            float bob = m * Lerp(0.016f * (float)Math.Cos(2 * s), -0.034f * (float)Math.Cos(2 * s) + 0.01f, r);
            HipsX = idle * 0.012f * shift;
            HipsY = (bob - m * r * 0.035f - idle * 0.004f) * (LegLength / 0.8f);
            HipsZ = 0f;
            float twist = m * Lerp(6f, 9f, r) * (float)Math.Sin(s);
            float roll = m * Lerp(3f, 2f, r) * (float)Math.Cos(s) + idle * 1.6f * shift;
            Set("hips", Rot.AroundY(twist) * Rot.AroundZ(roll));
            Set("spine", Rot.AroundX(m * Lerp(2f, 9f, r) + idle * 0.5f * breathe) * Rot.AroundY(-twist * 0.45f) * Rot.AroundZ(-roll * 0.5f));
            Set("chest", Rot.AroundX(m * r * 4f + idle * 1.2f * breathe) * Rot.AroundY(-twist * 0.5f));
            Set("upperChest", Rot.AroundX(idle * 0.8f * breathe));
            // the head keeps looking ahead; idle glances around now and then
            float look = idle * 7f * (float)Math.Sin(t * 0.37f) * (float)Math.Sin(t * 0.13f);
            Set("neck", Rot.AroundX(-m * r * 5f) * Rot.AroundY(look * 0.4f));
            Set("head", Rot.AroundX(-m * r * 4f + idle * 1.5f * (float)Math.Sin(t * 0.29f)) * Rot.AroundY(look * 0.6f - twist * 0.1f));

            // ---- arms: lowered from the T-pose, swinging against the legs, elbows bent more when running
            for (int side = 0; side < 2; side++)
            {
                float phi = side == 0 ? s + (float)Math.PI : s; // left arm with the right leg
                float sin = (float)Math.Sin(phi);
                float sign = side == 0 ? 1f : -1f;
                float down = Lerp(Lerp(73f, 75f, m), 66f, r * m) + idle * 1.2f * breathe;
                float pitch = m * Lerp(4f + 17f * sin, 14f + 34f * sin, r) + idle * 4f;
                float elbow = m * Lerp(14f + 14f * Math.Max(0f, sin), 78f + 14f * sin, r) + idle * 12f;
                string prefix = side == 0 ? "left" : "right";
                Set(prefix + "Shoulder", Rot.AroundZ(sign * (2f + idle * 0.6f * breathe)));
                Set(prefix + "UpperArm", Rot.AroundX(-pitch) * Rot.AroundZ(sign * down));
                Set(prefix + "LowerArm", Rot.AroundY(sign * elbow));
                Set(prefix + "Hand", Rot.AroundZ(sign * 6f) * Rot.AroundY(sign * 4f));
                // relaxed fingers, curled into the palm (towards -Y in the T-pose, so the same sign as "down")
                float curl = 22f + r * m * 30f;
                foreach (var f in Fingers)
                {
                    Set(prefix + f + "Proximal", Rot.AroundZ(sign * curl));
                    Set(prefix + f + "Intermediate", Rot.AroundZ(sign * curl * 1.3f));
                    Set(prefix + f + "Distal", Rot.AroundZ(sign * curl * 0.9f));
                }
                // the thumb points forward in the T-pose: tuck it down towards the palm instead of a thumbs-up
                Set(prefix + "ThumbProximal", Rot.AroundX(32f) * Rot.AroundZ(sign * 12f));
                Set(prefix + "ThumbIntermediate", Rot.AroundX(24f));
            }
        }

        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        private void Set(string bone, Rot q)
        {
            int i = IndexOf(bone);
            if (i < 0) return;
            Rotations[i * 4] = q.X;
            Rotations[i * 4 + 1] = q.Y;
            Rotations[i * 4 + 2] = q.Z;
            Rotations[i * 4 + 3] = q.W;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Smooth(float a, float b, float v)
        {
            var t = Math.Max(0f, Math.Min(1f, (v - a) / (b - a)));
            return t * t * (3f - 2f * t);
        }
    }

    /// <summary>Minimal quaternion with Unity's conventions (Rot.AroundY(90) turns +Z into +X), for engine-free code.</summary>
    public struct Rot
    {
        public float X, Y, Z, W;

        public Rot(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static Rot Axis(float degrees, float ax, float ay, float az)
        {
            var h = degrees * (float)Math.PI / 360f;
            var s = (float)Math.Sin(h);
            return new Rot(ax * s, ay * s, az * s, (float)Math.Cos(h));
        }

        public static Rot AroundX(float degrees) => Axis(degrees, 1, 0, 0);
        public static Rot AroundY(float degrees) => Axis(degrees, 0, 1, 0);
        public static Rot AroundZ(float degrees) => Axis(degrees, 0, 0, 1);

        /// <summary>a * b applies b first, then a (as in Unity).</summary>
        public static Rot operator *(Rot a, Rot b) => new Rot(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotates a vector (for tests and previews).</summary>
        public void Apply(float x, float y, float z, out float ox, out float oy, out float oz)
        {
            // v' = v + 2w(q x v) + 2 q x (q x v)
            float cx = Y * z - Z * y, cy = Z * x - X * z, cz = X * y - Y * x;
            float ccx = Y * cz - Z * cy, ccy = Z * cx - X * cz, ccz = X * cy - Y * cx;
            ox = x + 2f * (W * cx + ccx);
            oy = y + 2f * (W * cy + ccy);
            oz = z + 2f * (W * cz + ccz);
        }
    }
}
