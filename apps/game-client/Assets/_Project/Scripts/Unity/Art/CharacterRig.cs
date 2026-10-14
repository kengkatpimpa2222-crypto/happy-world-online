using System.Collections.Generic;
using UnityEngine;

namespace HappyWorld.Unity.Art
{
    /// <summary>
    /// The Kenney round-bodied character (CC0) dressed per player: arms and legs take the body colour,
    /// the head/torso gets a wash of the outfit colour (the smiley face stays readable), the antenna ball
    /// shows the hair colour. Walks and idles with the model's own animations whenever it moves.
    /// </summary>
    public sealed class CharacterRig : MonoBehaviour
    {
        public const string Key = "city/character";
        public const float Height = 1.9f;

        private NodeAnimator _anim;
        private Renderer _torso, _antenna;
        private readonly List<Renderer> _limbs = new List<Renderer>();
        private Texture2D _tex;
        private Vector3 _lastPos;
        private float _moving;
        /// <summary>Head/antenna parent: accessories (hats) go here so they bob with the body.</summary>
        public Transform Torso { get; private set; }
        public Vector3 HatLocalPos { get; private set; }

        public static CharacterRig Create(Transform parent)
        {
            var model = ModelLibrary.Spawn(Key, parent, Vector3.zero, Height);
            if (model == null) return null;
            var rig = model.AddComponent<CharacterRig>();
            var inst = model.GetComponent<ModelInstance>();
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "torso") rig._torso = r;
                else if (r.name == "antenna") rig._antenna = r;
                else rig._limbs.Add(r);
            }
            rig._tex = rig._torso != null ? rig._torso.sharedMaterial.mainTexture as Texture2D : null;
            rig.Torso = inst.Node("torso");
            var antenna = inst.Node("antenna");
            rig.HatLocalPos = antenna != null ? antenna.localPosition : Vector3.up * 0.5f;
            rig._anim = NodeAnimator.Attach(model);
            if (rig._anim != null) rig._anim.Play("idle");
            rig._lastPos = model.transform.position;
            return rig;
        }

        public void Dress(Color body, Color outfit, Color hair)
        {
            var bodyMat = World.CityBuilder.Mat(body);
            foreach (var l in _limbs) l.sharedMaterial = bodyMat;
            if (_torso != null) _torso.sharedMaterial = ModelLibrary.Material(_tex, Color.Lerp(outfit, Color.white, 0.45f));
            if (_antenna != null) _antenna.sharedMaterial = World.CityBuilder.Mat(hair);
        }

        private void LateUpdate()
        {
            if (_anim == null) return;
            var p = transform.position;
            var speed = new Vector2(p.x - _lastPos.x, p.z - _lastPos.z).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            _lastPos = p;
            // remote players move in network steps: keep walking briefly between updates instead of flickering
            _moving = speed > 0.4f ? 0.25f : _moving - Time.deltaTime;
            if (_moving > 0f) _anim.Play("walk", Mathf.Clamp(speed / 4f, 0.8f, 1.6f));
            else _anim.Play("idle");
        }
    }
}
