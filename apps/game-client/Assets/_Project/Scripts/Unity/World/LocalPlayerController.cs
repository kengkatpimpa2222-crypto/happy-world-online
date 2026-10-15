using UnityEngine;

namespace HappyWorld.Unity.World
{
    /// <summary>
    /// Moves the local avatar with a CharacterController from the virtual joystick (touch) or WASD/arrow keys.
    /// Movement is camera-relative and capped below the server's 8 units/s.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayerController : MonoBehaviour
    {
        public float Speed = 6f;
        public UI.VirtualJoystick Joystick;
        public float Width = 60, Height = 60;
        private CharacterController _cc;
        private float _vy;

        public bool InputEnabled = true;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.center = new Vector3(0, 1f, 0);
            _cc.height = 2f;
            _cc.radius = 0.4f;
        }

        /// <summary>Server (x, y) position of the player.</summary>
        public Vector2 ServerPos => new Vector2(transform.position.x, transform.position.z);

        public void Teleport(float x, float y)
        {
            _cc.enabled = false;
            transform.position = new Vector3(x, 0.05f, y);
            _cc.enabled = true;
        }

        private void Update()
        {
            Vector2 input = Vector2.zero;
            if (InputEnabled)
            {
                input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                if (Joystick != null && Joystick.Value.sqrMagnitude > input.sqrMagnitude) input = Joystick.Value;
                if (input.sqrMagnitude > 1) input.Normalize();
            }

            var cam = Camera.main;
            Vector3 fwd = Vector3.forward, right = Vector3.right;
            if (cam != null)
            {
                fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
                right = cam.transform.right; right.y = 0; right.Normalize();
            }
            var move = (fwd * input.y + right * input.x) * Speed;
            _vy = _cc.isGrounded ? -1f : _vy - 20f * Time.deltaTime;
            move.y = _vy;
            _cc.Move(move * Time.deltaTime);

            var p = transform.position;
            p.x = Mathf.Clamp(p.x, 0.3f, Width - 0.3f);
            p.z = Mathf.Clamp(p.z, 0.3f, Height - 0.3f);
            if (p != transform.position) Teleport(p.x, p.z);

            var flat = new Vector3(move.x, 0, move.z);
            if (flat.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 12f * Time.deltaTime);
        }
    }

    /// <summary>Third-person follow camera from above and behind.</summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        public Transform Target;
        public Vector3 Offset = new Vector3(0, 11f, -9f);
        /// <summary>Point looked at, relative to the target (the player's chest by default).</summary>
        public Vector3 LookOffset = Vector3.up;

        private void LateUpdate()
        {
            if (Target == null) return;
            var want = Target.position + Offset;
            transform.position = Vector3.Lerp(transform.position, want, 8f * Time.deltaTime);
            transform.rotation = Quaternion.LookRotation(Target.position + LookOffset - transform.position);
        }
    }
}
