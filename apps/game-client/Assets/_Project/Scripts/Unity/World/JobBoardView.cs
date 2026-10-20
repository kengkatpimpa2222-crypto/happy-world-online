using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using HappyWorld.Unity.Art;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Phase 12: the workplaces of the zone the player is in, each with a "now hiring" board where the
    /// "work here" prompt appears (positions come from GET /jobs). Built from primitives plus a Kenney model
    /// where one fits, so the zone always builds. The Countryside also gets the gate out to the expedition sites.
    /// </summary>
    public sealed class JobBoardView : MonoBehaviour
    {
        public static JobBoardView Create(Transform zoneRoot, Zone zone, IEnumerable<JobInfo> jobs)
        {
            var go = new GameObject("Jobs");
            go.transform.SetParent(zoneRoot, false);
            var v = go.AddComponent<JobBoardView>();
            if (jobs != null)
                foreach (var j in jobs)
                    if (j.zone == zone.id) v.Workplace(j);
            if (zone.id == JobPresenter.ExploreGateZone) v.ExploreGate();
            return v;
        }

        private void Workplace(JobInfo j)
        {
            var at = new Vector3(j.x, 0, j.y);
            var root = new GameObject("Job_" + j.job).transform;
            root.SetParent(transform, false);
            root.localPosition = at;
            switch (j.job)
            {
                case "CASHIER": Minimart(root); break;
                case "FARMHAND": Farm(root); break;
                case "PACKER": Plant(root); break;
            }
            // the board and the orange work spot at (x, y), where the prompt appears; the building stands behind
            var post = new Color(0.45f, 0.3f, 0.2f);
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(-0.9f, 0.8f, 1.2f), new Vector3(0.12f, 1.6f, 0.12f), post);
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(0.9f, 0.8f, 1.2f), new Vector3(0.12f, 1.6f, 0.12f), post);
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(0, 1.7f, 1.2f), new Vector3(2.2f, 1f, 0.1f), j.unlocked ? new Color(1f, 0.9f, 0.55f) : new Color(0.75f, 0.75f, 0.78f), "Board");
            var ring = CityBuilder.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.03f, 0), new Vector3(1.8f, 0.02f, 1.8f), new Color(1f, 0.62f, 0.25f), "WorkSpot");
            Destroy(ring.GetComponent<Collider>());
            CityBuilder.Label(root, new Vector3(0, 2.9f, 1.2f), JobPresenter.BoardSign(j), 0.16f, Color.white);
        }

        private static void Minimart(Transform root)
        {
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(0, 1.6f, 4.5f), new Vector3(6f, 3.2f, 4f), new Color(0.97f, 0.97f, 0.95f));
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(0, 3.0f, 2.45f), new Vector3(6.2f, 0.5f, 0.1f), new Color(0.2f, 0.65f, 0.35f)); // shop sign stripe
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(0, 1.1f, 2.48f), new Vector3(1.6f, 2.2f, 0.05f), new Color(0.6f, 0.85f, 1f));  // glass door
        }

        private static void Farm(Transform root)
        {
            // a barn and rows of crops with a few crates
            CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(-5f, 1.5f, 6f), new Vector3(4f, 3f, 4f), new Color(0.75f, 0.25f, 0.22f));
            var roof = CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(-5f, 3.3f, 6f), new Vector3(2.9f, 2.9f, 4.2f), new Color(0.35f, 0.3f, 0.28f));
            roof.transform.localRotation = Quaternion.Euler(0, 0, 45);
            for (int r = 0; r < 4; r++)
            {
                var row = CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(2.5f, 0.15f, 4f + r * 1.6f), new Vector3(6f, 0.3f, 0.8f), new Color(0.45f, 0.32f, 0.2f));
                Destroy(row.GetComponent<Collider>());
                for (int k = 0; k < 5; k++)
                {
                    var plant = CityBuilder.Prim(PrimitiveType.Sphere, root, new Vector3(0.2f + k * 1.2f, 0.55f, 4f + r * 1.6f), Vector3.one * 0.6f, new Color(0.3f, 0.65f, 0.3f));
                    Destroy(plant.GetComponent<Collider>());
                }
            }
            for (int c = 0; c < 3; c++)
                CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(-2.2f + c * 0.8f, 0.3f, 1.8f), new Vector3(0.7f, 0.6f, 0.7f), new Color(0.8f, 0.62f, 0.38f));
        }

        private static void Plant(Transform root)
        {
            if (ModelLibrary.SpawnScaled("city/building-garage", root, new Vector3(0, 0, 6f), 6f) == null)
                CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(0, 2.5f, 6f), new Vector3(9f, 5f, 6f), new Color(0.7f, 0.74f, 0.78f));
            CityBuilder.Prim(PrimitiveType.Cylinder, root, new Vector3(4f, 3.5f, 8f), new Vector3(0.8f, 3.5f, 0.8f), new Color(0.55f, 0.55f, 0.6f)); // chimney
            // conveyor belt with boxes
            var belt = CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(-3.5f, 0.5f, 2f), new Vector3(3f, 0.2f, 1f), new Color(0.25f, 0.25f, 0.3f));
            Destroy(belt.GetComponent<Collider>());
            for (int b = 0; b < 3; b++)
                CityBuilder.Prim(PrimitiveType.Cube, root, new Vector3(-4.5f + b * 1f, 0.9f, 2f), Vector3.one * (0.3f + b * 0.1f), new Color(0.82f, 0.65f, 0.42f));
        }

        private void ExploreGate()
        {
            var at = new Vector3(JobPresenter.ExploreGateX, 0, JobPresenter.ExploreGateY);
            var wood = new Color(0.55f, 0.38f, 0.22f);
            CityBuilder.Prim(PrimitiveType.Cube, transform, at + new Vector3(-1.6f, 1.4f, 0), new Vector3(0.3f, 2.8f, 0.3f), wood);
            CityBuilder.Prim(PrimitiveType.Cube, transform, at + new Vector3(1.6f, 1.4f, 0), new Vector3(0.3f, 2.8f, 0.3f), wood);
            CityBuilder.Prim(PrimitiveType.Cube, transform, at + new Vector3(0, 2.9f, 0), new Vector3(3.8f, 0.4f, 0.4f), wood);
            var ring = CityBuilder.Prim(PrimitiveType.Cylinder, transform, at + new Vector3(0, 0.03f, 0), new Vector3(1.8f, 0.02f, 1.8f), new Color(0.4f, 0.75f, 0.45f), "ExploreSpot");
            Destroy(ring.GetComponent<Collider>());
            CityBuilder.Label(transform, at + Vector3.up * 3.7f, "ทางไปจุดสำรวจ", 0.16f, Color.white);
        }
    }
}
