using HappyWorld.Core.Api;
using Avatar = HappyWorld.Core.Api.Avatar; // UnityEngine also has an Avatar type
using HappyWorld.Unity.Art;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// A player: the Kenney character dressed in the avatar's colours (<see cref="CharacterRig"/>), nameplate and
    /// chat bubble. Falls back to the original primitive figure if the model can't load.
    /// </summary>
    public sealed class AvatarView : MonoBehaviour
    {
        public string UserId;
        private Renderer _body, _shirt, _hair, _hat;
        private CharacterRig _rig;
        private TextMesh _name, _bubble;
        private float _bubbleUntil;

        public static AvatarView Create(Transform parent, string userId, string displayName, int level)
        {
            var root = new GameObject("Avatar_" + displayName);
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<AvatarView>();
            v.UserId = userId;
            v._rig = CharacterRig.Create(root.transform);
            if (v._rig != null)
            {
                v._hat = CityBuilder.Prim(PrimitiveType.Cylinder, v._rig.Torso != null ? v._rig.Torso : root.transform, Vector3.zero, Vector3.one, Color.yellow, "Hat").GetComponent<Renderer>();
                var ht = v._hat.transform;
                ht.localPosition = v._rig.HatLocalPos + Vector3.down * 0.08f; // brim around the antenna
                ht.localScale = new Vector3(0.75f, 0.02f, 0.75f);
            }
            else BuildPrimitives(v, root);
            // one generous capsule on the root instead of the primitives' own colliders
            foreach (var c in root.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 1f, 0);
            col.height = 2.2f;
            col.radius = 0.5f;
            col.isTrigger = true; // tappable, but nobody gets stuck walking into another player
            v._name = CityBuilder.Label(root.transform, new Vector3(0, 2.6f, 0), "", 0.12f, Color.white);
            v._bubble = CityBuilder.Label(root.transform, new Vector3(0, 3.1f, 0), "", 0.14f, new Color(1f, 1f, 0.6f));
            v.SetName(displayName, level, false);
            return v;
        }

        private static void BuildPrimitives(AvatarView v, GameObject root)
        {
            v._body = CityBuilder.Prim(PrimitiveType.Capsule, root.transform, new Vector3(0, 1f, 0), new Vector3(0.8f, 0.5f, 0.8f), Color.white, "Legs").GetComponent<Renderer>();
            v._shirt = CityBuilder.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 1.15f, 0), new Vector3(0.75f, 0.6f, 0.5f), Color.white, "Shirt").GetComponent<Renderer>();
            var head = CityBuilder.Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 1.75f, 0), new Vector3(0.6f, 0.6f, 0.6f), Color.white, "Head");
            v._hair = CityBuilder.Prim(PrimitiveType.Sphere, root.transform, new Vector3(0, 1.9f, -0.05f), new Vector3(0.64f, 0.4f, 0.64f), Color.black, "Hair").GetComponent<Renderer>();
            v._hat = CityBuilder.Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0, 2.1f, 0), new Vector3(0.8f, 0.04f, 0.8f), Color.yellow, "Hat").GetComponent<Renderer>();
            CityBuilder.Prim(PrimitiveType.Cube, root.transform, new Vector3(0, 1.75f, 0.3f), new Vector3(0.3f, 0.08f, 0.05f), Color.black, "Face");
            head.GetComponent<Renderer>().sharedMaterial = CityBuilder.Mat(new Color(1f, 0.85f, 0.7f));
        }

        public void SetName(string displayName, int level, bool isFriend)
        {
            _name.text = (isFriend ? "♥ " : "") + displayName + "  Lv." + level;
        }

        public void ApplyAvatar(Avatar a) => Apply(a?.bodyColor, a?.outfitItemId, a?.hairItemId, a?.accessoryItemId);

        public void ApplyAvatar(JToken a)
        {
            if (a == null || a.Type != JTokenType.Object) { Apply(null, null, null, null); return; }
            Apply((string)a["bodyColor"], (string)a["outfitItemId"], (string)a["hairItemId"], (string)a["accessoryItemId"]);
        }

        private void Apply(string bodyColor, string outfit, string hair, string accessory)
        {
            Color body;
            if (string.IsNullOrEmpty(bodyColor) || !ColorUtility.TryParseHtmlString(bodyColor, out body)) body = new Color(0.4f, 0.6f, 1f);
            if (_rig != null)
            {
                _rig.Dress(body, ItemColor(outfit, body), ItemColor(hair, new Color(0.3f, 0.2f, 0.1f)));
                _hat.gameObject.SetActive(accessory == "acc_straw_hat");
                _hat.sharedMaterial = CityBuilder.Mat(new Color(0.95f, 0.85f, 0.5f));
                return;
            }
            _body.sharedMaterial = CityBuilder.Mat(body);
            _shirt.sharedMaterial = CityBuilder.Mat(ItemColor(outfit, body * 0.8f));
            _hair.sharedMaterial = CityBuilder.Mat(ItemColor(hair, new Color(0.3f, 0.2f, 0.1f)));
            _hat.gameObject.SetActive(accessory == "acc_straw_hat");
            if (accessory == "acc_straw_hat") _hat.sharedMaterial = CityBuilder.Mat(new Color(0.95f, 0.85f, 0.5f));
        }

        /// <summary>Placeholder look per item id until real meshes exist.</summary>
        private static Color ItemColor(string itemId, Color fallback)
        {
            switch (itemId)
            {
                case "outfit_basic_tee": return new Color(0.95f, 0.95f, 0.95f);
                case "outfit_school_uniform": return new Color(0.2f, 0.3f, 0.6f);
                case "outfit_thai_silk": return new Color(0.75f, 0.2f, 0.45f);
                case "outfit_star_idol": return new Color(1f, 0.75f, 0.2f);
                case "hair_short_brown": return new Color(0.4f, 0.25f, 0.12f);
                case "hair_long_black": return new Color(0.08f, 0.08f, 0.08f);
                default: return fallback;
            }
        }

        public void ShowChat(string text, float seconds = 4f)
        {
            _bubble.text = text;
            _bubbleUntil = Time.time + seconds;
        }

        private void Update()
        {
            if (_bubble.text.Length > 0 && Time.time > _bubbleUntil) _bubble.text = "";
        }
    }
}
