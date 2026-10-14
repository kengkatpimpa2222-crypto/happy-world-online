using System;
using System.IO;
using System.Linq;
using HappyWorld.Core.Art;
using Xunit;

namespace HappyWorld.Core.Tests
{
    public class ArtTests
    {
        private static readonly string ModelsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/_Project/Resources/Art/Models"));

        private static GlbModel Load(string key) => GlbModel.Read(File.ReadAllBytes(Path.Combine(ModelsDir, key + ".bytes")));

        [Fact]
        public void EveryBundledModelParses()
        {
            var files = Directory.GetFiles(ModelsDir, "*.bytes", SearchOption.AllDirectories);
            Assert.True(files.Length >= 10, "models missing: " + ModelsDir);
            foreach (var f in files)
            {
                var m = GlbModel.Read(File.ReadAllBytes(f));
                Assert.NotEmpty(m.Meshes);
                Assert.NotEmpty(m.Roots);
                Assert.Equal("colormap.png", m.ImageName);
                // the texture the model points at ships next to it
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(f), "colormap.png")), f);
                foreach (var p in m.Meshes.SelectMany(x => x.Primitives))
                {
                    Assert.Equal(p.VertexCount * 3, p.Normals.Length);
                    Assert.Equal(p.VertexCount * 2, p.Uvs.Length);
                    Assert.All(p.Indices, i => Assert.InRange(i, 0, p.VertexCount - 1));
                }
            }
        }

        [Fact]
        public void BuildingSitsOnTheGroundInAOneUnitTile()
        {
            float[] min, max;
            Load("city/building-small-a").Bounds(out min, out max);
            Assert.InRange(min[1], -0.01f, 0.01f);
            Assert.InRange(max[0] - min[0], 0.99f, 1.01f);
            Assert.InRange(max[1], 0.9f, 1.0f);
        }

        [Fact]
        public void WindingMatchesNormalsAfterConversionToUnitySpace()
        {
            // Unity draws clockwise faces; for the converted data the plain cross product must agree with the normals
            foreach (var key in new[] { "city/building-small-c", "city/character", "arena/tree" })
            {
                int agree = 0, total = 0;
                foreach (var p in Load(key).Meshes.SelectMany(x => x.Primitives))
                    for (int t = 0; t < p.Indices.Length; t += 3)
                    {
                        int a = p.Indices[t] * 3, b = p.Indices[t + 1] * 3, c = p.Indices[t + 2] * 3;
                        float ux = p.Positions[b] - p.Positions[a], uy = p.Positions[b + 1] - p.Positions[a + 1], uz = p.Positions[b + 2] - p.Positions[a + 2];
                        float vx = p.Positions[c] - p.Positions[a], vy = p.Positions[c + 1] - p.Positions[a + 1], vz = p.Positions[c + 2] - p.Positions[a + 2];
                        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                        float dot = nx * p.Normals[a] + ny * p.Normals[a + 1] + nz * p.Normals[a + 2];
                        if (Math.Abs(dot) < 1e-9) continue;
                        total++;
                        if (dot > 0) agree++;
                    }
                Assert.True(agree > total * 0.95, key + ": " + agree + "/" + total);
            }
        }

        [Fact]
        public void CharacterHasRigidPartsAndWalkCycle()
        {
            var m = Load("city/character");
            foreach (var part in new[] { "leg-left", "leg-right", "torso", "arm-left", "arm-right" }) Assert.True(m.NodeIndex(part) >= 0, part);
            // glTF +X becomes Unity -X: the left leg (glTF x=+0.12) now sits at x=-0.12
            Assert.Equal(-0.12f, m.Nodes[m.NodeIndex("leg-left")].T[0], 2);
            var walk = m.Clip("walk");
            Assert.NotNull(walk);
            Assert.InRange(walk.Duration, 0.2f, 5f);
            Assert.NotNull(m.Clip("idle"));
            var rot = walk.Channels.First(c => c.Path == "rotation");
            var q = new float[4];
            rot.Sample(walk.Duration * 0.37f, q);
            Assert.InRange(Math.Sqrt(q.Sum(v => v * v)), 0.999, 1.001);
        }

        [Fact]
        public void ChannelSamplingInterpolatesAndClamps()
        {
            var ch = new GlbChannel { Path = "translation", Width = 3, Times = new[] { 0f, 1f }, Values = new[] { 0f, 0f, 0f, 2f, 4f, 6f } };
            var v = new float[3];
            ch.Sample(0.5f, v);
            Assert.Equal(new[] { 1f, 2f, 3f }, v);
            ch.Sample(9f, v);
            Assert.Equal(new[] { 2f, 4f, 6f }, v);
            ch.Step = true;
            ch.Sample(0.9f, v);
            Assert.Equal(new[] { 0f, 0f, 0f }, v);
        }

        [Fact]
        public void RejectsGarbage()
        {
            Assert.Throws<InvalidDataException>(() => GlbModel.Read(new byte[40]));
            Assert.Throws<InvalidDataException>(() => GlbModel.Read(null));
        }
    }
}
