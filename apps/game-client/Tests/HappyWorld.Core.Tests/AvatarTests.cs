using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HappyWorld.Core.Art;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>The anime avatar parts (built by Tools/art/build_avatars.py), their item mapping and the gait.</summary>
    public class AvatarTests
    {
        private static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/_Project/Resources/Art/Models"));

        private static GlbModel Load(string key) => GlbModel.Read(File.ReadAllBytes(Path.Combine(Dir, key + ".bytes")));

        private static readonly string[] Bodies = { AvatarLooks.Female, AvatarLooks.Male };
        private static readonly string[] Outfits = { null, "outfit_basic_tee", "outfit_school_uniform", "outfit_thai_silk", "outfit_star_idol", "outfit_unknown" };
        private static readonly string[] Hairs = { null, "hair_short_brown", "hair_long_black" };

        private static IEnumerable<string> AllKeys()
        {
            foreach (var b in Bodies)
            {
                yield return AvatarLooks.Base(b);
                foreach (var o in Outfits) yield return AvatarLooks.Outfit(b, o);
                foreach (var h in Hairs) yield return AvatarLooks.Hair(b, h);
                yield return AvatarLooks.Hat(b, "acc_straw_hat");
            }
        }

        [Fact]
        public void EveryLookTheGameCanAskForShipsAsAValidSkinnedPart()
        {
            foreach (var key in AllKeys().Distinct())
            {
                var m = Load(key);
                var meshNodes = m.Nodes.Where(n => n.Mesh >= 0).ToList();
                Assert.Single(meshNodes);
                var node = meshNodes[0];
                Assert.True(node.Skin >= 0, key + " is not skinned");
                var joints = m.Skins[node.Skin].Joints;
                Assert.Contains(m.Nodes, n => n.Name == "Root");
                foreach (var p in m.Meshes[node.Mesh].Primitives)
                {
                    Assert.Equal(p.VertexCount * 4, p.Joints.Length);
                    Assert.All(p.Joints, j => Assert.InRange(j, 0, joints.Length - 1));
                    for (int v = 0; v < p.VertexCount; v++)
                        Assert.InRange(p.Weights[v * 4] + p.Weights[v * 4 + 1] + p.Weights[v * 4 + 2] + p.Weights[v * 4 + 3], 0.98f, 1.02f);
                    Assert.All(p.Indices, i => Assert.InRange(i, 0, p.VertexCount - 1));
                    Assert.NotNull(p.Image);
                    Assert.True(File.Exists(Path.Combine(Dir, "avatar", p.Image)), key + " texture missing: " + p.Image);
                    Assert.NotNull(p.MaterialExtras?["shade"]);
                }
            }
        }

        [Fact]
        public void PartsShareTheirBodysSkeletonSoTheyFitAnyCharacter()
        {
            foreach (var b in Bodies)
            {
                var basis = Load(AvatarLooks.Base(b));
                var rest = basis.Nodes.Where(n => n.Mesh < 0).ToDictionary(n => n.Name, n => n.T);
                foreach (var key in AllKeys().Where(k => k.StartsWith("avatar/" + b + "_")))
                    foreach (var n in Load(key).Nodes.Where(n => n.Mesh < 0 && rest.ContainsKey(n.Name)))
                        for (int k = 0; k < 3; k++)
                            Assert.True(Math.Abs(n.T[k] - rest[n.Name][k]) < 1e-4f, key + ": " + n.Name + " moved");
            }
        }

        [Fact]
        public void BaseHasAFullHumanoidMapFacialShapesAndFacesForward()
        {
            foreach (var b in Bodies)
            {
                var m = Load(AvatarLooks.Base(b));
                var humanoid = (JObject)m.Extras["humanoid"];
                var names = new HashSet<string>(m.Nodes.Select(n => n.Name));
                foreach (var bone in new[] { "hips", "spine", "chest", "neck", "head", "leftUpperArm", "leftLowerArm", "leftHand", "rightUpperArm", "rightLowerArm", "rightHand", "leftUpperLeg", "leftLowerLeg", "leftFoot", "rightUpperLeg", "rightLowerLeg", "rightFoot" })
                    Assert.True(humanoid[bone] != null && names.Contains((string)humanoid[bone]), b + " lacks " + bone);
                // every bone the gait drives exists on the skeleton
                foreach (var bone in HumanGait.Bones) Assert.True(names.Contains((string)humanoid[bone]), b + " gait bone " + bone);
                var face = m.Meshes[m.Nodes.First(n => n.Mesh >= 0).Mesh];
                Assert.Contains("blink", face.TargetNames);
                Assert.Contains("joy", face.TargetNames);
                Assert.NotEmpty((JArray)m.Extras["colliders"]);
                // the character's left arm points to -X in Unity space, so it faces +Z like every Unity object
                var left = m.Nodes.First(n => n.Name == (string)humanoid["leftUpperArm"]);
                var leftShoulder = m.Nodes.First(n => n.Name == (string)humanoid["leftShoulder"]);
                Assert.True(left.T[0] + leftShoulder.T[0] < 0, b + " is mirrored or turned round");
            }
        }

        [Fact]
        public void SwayChainsPointAtBonesThatExist()
        {
            foreach (var key in AllKeys().Distinct())
            {
                var m = Load(key);
                var names = new HashSet<string>(m.Nodes.Select(n => n.Name));
                var springs = m.Extras["springs"] as JArray;
                if (springs == null) continue;
                foreach (JObject s in springs)
                    foreach (var bone in (JArray)s["bones"]) Assert.Contains((string)bone, names);
            }
            Assert.NotEmpty((JArray)Load(AvatarLooks.Hair(AvatarLooks.Female, "hair_long_black")).Extras["springs"]);
        }

        [Fact]
        public void ItemsMapToLooksAndUnknownItemsFallBack()
        {
            Assert.Equal("avatar/female_outfit_school_uniform", AvatarLooks.Outfit("female", "outfit_school_uniform"));
            Assert.Equal("avatar/male_outfit_casual", AvatarLooks.Outfit("male", "outfit_basic_tee"));
            Assert.Equal("avatar/female_outfit_casual", AvatarLooks.Outfit("female", "outfit_from_the_future"));
            Assert.Equal("avatar/female_hair_long_black", AvatarLooks.Hair("female", "hair_long_black"));
            Assert.Equal("avatar/male_hair_messy_black", AvatarLooks.Hair("male", "hair_long_black"));
            Assert.Null(AvatarLooks.Hat("female", null));
            Assert.Null(AvatarLooks.Hat("female", "acc_rainbow_wings"));
        }

        [Fact]
        public void OldAvatarsGetAStableBodyTypeAndAChosenOneWins()
        {
            Assert.Equal("male", AvatarLooks.BodyType("male", "anyone"));
            Assert.Equal("female", AvatarLooks.BodyType("female", "anyone"));
            var picked = AvatarLooks.BodyType(null, "user-123");
            Assert.Equal(picked, AvatarLooks.BodyType("robot", "user-123"));
            Assert.Equal(picked, AvatarLooks.BodyType(null, "user-123"));
            var both = Enumerable.Range(0, 40).Select(i => AvatarLooks.BodyType(null, "u" + i)).Distinct().Count();
            Assert.Equal(2, both);
        }

        [Fact]
        public void SkinToneTintsOnlyForSkinColours()
        {
            float r, g, b;
            AvatarLooks.SkinTint("#F5C6A5", out r, out g, out b);
            Assert.InRange(r, 0.99f, 1.01f);
            Assert.InRange(b, 0.99f, 1.01f);
            AvatarLooks.SkinTint("#8D5524", out r, out g, out b);
            Assert.True(r < 0.7f && b < r);
            AvatarLooks.SkinTint("#6FA8DC", out r, out g, out b); // an old blue "body colour"
            Assert.Equal(1f, r);
            Assert.Equal(1f, b);
            AvatarLooks.SkinTint("nonsense", out r, out g, out b);
            Assert.Equal(1f, g);
            foreach (var tone in AvatarLooks.SkinTones)
            {
                AvatarLooks.SkinTint(tone, out r, out g, out b);
                Assert.True(r <= 1.08f && r >= 0.3f, tone);
            }
        }

        // ---------- gait ----------

        private static float Angle(HumanGait g, string bone, float vx, float vy, float vz, out float ox, out float oy, out float oz)
        {
            int i = HumanGait.IndexOf(bone);
            var q = new Rot(g.Rotations[i * 4], g.Rotations[i * 4 + 1], g.Rotations[i * 4 + 2], g.Rotations[i * 4 + 3]);
            q.Apply(vx, vy, vz, out ox, out oy, out oz);
            return 0;
        }

        /// <summary>Where the thigh points: z &gt; 0 = forward.</summary>
        private static float ThighForward(HumanGait g, string side)
        {
            float x, y, z;
            Angle(g, side + "UpperLeg", 0, -1, 0, out x, out y, out z);
            return z;
        }

        [Fact]
        public void StandingStillIsCalmAndSymmetric()
        {
            var g = new HumanGait();
            for (int i = 0; i < 120; i++) g.Step(1 / 60f, 0f);
            Assert.Equal(0f, g.MoveWeight);
            Assert.InRange(ThighForward(g, "left"), -0.01f, 0.01f);
            Assert.InRange(ThighForward(g, "right"), -0.01f, 0.01f);
            // arms hang down, not out in the T-pose
            float x, y, z;
            Angle(g, "leftUpperArm", -1, 0, 0, out x, out y, out z);
            Assert.True(y < -0.9f, "left arm still sticks out: " + y);
            Assert.True(x < 0f, "left arm crossed to the right side");
            Angle(g, "rightUpperArm", 1, 0, 0, out x, out y, out z);
            Assert.True(y < -0.9f && x > 0f);
        }

        [Fact]
        public void WalkingSwingsLegsInTurnAndArmsAgainstThem()
        {
            var g = new HumanGait();
            float maxLeft = -1, minLeft = 1;
            bool alwaysOpposite = true;
            for (int i = 0; i < 240; i++)
            {
                g.Step(1 / 60f, 1.4f / 60f);
                if (i < 60) continue;
                var l = ThighForward(g, "left");
                var r = ThighForward(g, "right");
                maxLeft = Math.Max(maxLeft, l);
                minLeft = Math.Min(minLeft, l);
                if (Math.Abs(l) > 0.15f && Math.Sign(l) == Math.Sign(r)) alwaysOpposite = false;
                // left arm swings with the right leg: when the left leg is well forward, the left hand is behind
                float x, y, z;
                Angle(g, "leftUpperArm", -1, 0, 0, out x, out y, out z);
                if (l > 0.3f) Assert.True(z < 0.05f, "left arm swings with the left leg");
            }
            Assert.True(maxLeft > 0.3f && minLeft < -0.3f, "legs barely move: " + minLeft + ".." + maxLeft);
            Assert.True(alwaysOpposite, "both legs forward at once");
            Assert.True(g.MoveWeight > 0.99f);
            Assert.True(g.RunWeight < 0.05f, "a stroll should not be a run");
        }

        [Fact]
        public void RunningBlendsInWithSpeedAndBendsTheElbows()
        {
            var g = new HumanGait();
            for (int i = 0; i < 120; i++) g.Step(1 / 60f, 5f / 60f);
            Assert.True(g.RunWeight > 0.99f);
            float maxElbow = 0;
            for (int i = 0; i < 60; i++)
            {
                g.Step(1 / 60f, 5f / 60f);
                float x, y, z;
                Angle(g, "leftLowerArm", -1, 0, 0, out x, out y, out z);
                maxElbow = Math.Max(maxElbow, z); // forearm pointing forward = bent elbow
            }
            Assert.True(maxElbow > 0.8f, "running with straight arms");
            // and it settles back to idle when the player stops
            for (int i = 0; i < 120; i++) g.Step(1 / 60f, 0f);
            Assert.True(g.MoveWeight < 0.01f);
        }

        [Fact]
        public void RotUsesUnityConventions()
        {
            float x, y, z;
            Rot.AroundY(90).Apply(0, 0, 1, out x, out y, out z); // Quaternion.Euler(0, 90, 0) * forward = right
            Assert.InRange(x, 0.999f, 1.001f);
            Rot.AroundX(90).Apply(0, 0, 1, out x, out y, out z); // Euler(90, 0, 0) * forward = down
            Assert.InRange(y, -1.001f, -0.999f);
            (Rot.AroundY(90) * Rot.AroundX(90)).Apply(0, 0, 1, out x, out y, out z); // X first, then Y
            Assert.InRange(y, -1.001f, -0.999f);
        }
    }
}
