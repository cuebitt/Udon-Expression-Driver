using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace UdonExpressionDriver
{
    /// <summary>
    /// World analog of VRCFury's Armature Link: while worn, the prop sticks to the
    /// wearer's selected humanoid bone. Non-destructive: the prop is never reparented
    /// into the avatar; the owner writes its transform from the wearer's bone every
    /// frame and VRC_ObjectSync propagates + interpolates it to everyone else.
    ///
    /// Activation uses the prop's VRC_Pickup: grab to wear, Use to toggle, let go to
    /// release. Ownership transfer or the wearer leaving also releases the prop.
    ///
    /// No variables are Udon-synced here; the transform is synced by VRC_ObjectSync,
    /// so this behavior uses BehaviourSyncMode.None (Manual Udon variables are not
    /// allowed on a GameObject with VRC_ObjectSync).
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UEDArmatureLink : UEDBehaviour
    {
        [Header("Attach Target")]
        [Tooltip("Humanoid bone on the wearer's avatar the prop sticks to.")]
        [SerializeField] private HumanBodyBones targetBone = HumanBodyBones.Hips;
        [Tooltip("Child transform marking the contact point that lands on the bone. Falls back to the offsets when null.")]
        [SerializeField] private Transform attachPoint;
        [Tooltip("Offset from the bone when no attach point is set.")]
        [SerializeField] private Vector3 positionOffset;
        [Tooltip("Rotation offset from the bone when no attach point is set.")]
        [SerializeField] private Vector3 rotationOffset;

        [Tooltip("Wear the prop automatically when it is picked up.")]
        [SerializeField] private bool autoWearOnPickup = true;
        [Tooltip("Toggle wear when Use is pressed while holding the prop.")]
        [SerializeField] private bool wearOnUse = true;
        [Tooltip("Release the prop when the grab is let go while worn. Disable to keep it worn until Use or an external trigger removes it.")]
        [SerializeField] private bool releaseUnwears = true;
        [Tooltip("Toggle wear when the prop is interacted with. Keep off if another component owns Interact.")]
        [SerializeField] private bool interactTogglesWear;
        [Tooltip("Disable colliders while worn so the prop doesn't block players. Re-enabled on release.")]
        [SerializeField] private bool disableCollidersWhileWorn = true;
        [Tooltip("Extra GameObjects the wearer must own while worn (e.g. a child UEDFullController whose synced parameters the wearer writes). Ownership is transferred to the wearer on wear.")]
        [SerializeField] private GameObject[] ownedObjects = new GameObject[0];

        [Tooltip("Behavior notified on wear/unwear via SendCustomEvent.")]
        [SerializeField] private UdonSharpBehaviour wearEventHandler;
        [SerializeField] private string wornEventName;
        [SerializeField] private string unwornEventName;

        [Header("Internal")]
        [SerializeField] private VRCPickup propPickup;
        [SerializeField] private Rigidbody propRigidbody;

        private Collider[] _colliders = new Collider[0];
        private bool _worn;
        private VRCPlayerApi _wearer;
        private bool _hasAttachPoint;
        private Vector3 _attachPos;
        private Quaternion _attachRot;

        private void Start()
        {
            if (propPickup == null) propPickup = GetComponent<VRCPickup>();
            if (propRigidbody == null) propRigidbody = GetComponent<Rigidbody>();
            _colliders = GetComponents<Collider>();

            _hasAttachPoint = attachPoint != null;
            if (_hasAttachPoint)
            {
                _attachPos = attachPoint.localPosition;
                _attachRot = attachPoint.localRotation;
            }
        }

        public override void OnPickup()
        {
            if (autoWearOnPickup) _Wear();
        }

        public override void OnPickupUseDown()
        {
            if (wearOnUse) _ToggleWear();
        }

        public override void OnDrop()
        {
            // Letting go while worn releases the prop unless releaseUnwears is off
            // (sticky wear, removed via Use while held or an external trigger).
            if (_worn && releaseUnwears) _Unwear();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            // Only release when someone else takes the prop; taking it yourself
            // (e.g. grabbing to wear) must not immediately unwear.
            if (_worn && !player.isLocal) _Unwear();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (_worn && _wearer != null && player.playerId == _wearer.playerId) _Unwear();
        }

        public override void Interact()
        {
            if (!interactTogglesWear) return;
            _ToggleWear();
        }

        public override void PostLateUpdate()
        {
            if (!_worn) return;
            if (_wearer == null || !_wearer.IsValid()) return;
            if (!Networking.IsOwner(gameObject)) return;

            var boneRotation = _wearer.GetBoneRotation(targetBone);
            var bonePosition = _wearer.GetBonePosition(targetBone);

            Quaternion targetRotation;
            Vector3 targetPosition;
            if (_hasAttachPoint)
            {
                targetRotation = boneRotation * Quaternion.Inverse(_attachRot);
                targetPosition = bonePosition - targetRotation * _attachPos;
            }
            else
            {
                targetRotation = boneRotation * Quaternion.Euler(rotationOffset);
                targetPosition = bonePosition + targetRotation * positionOffset;
            }

            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        /// <summary>Wears the prop on the local player. The caller must own the prop.</summary>
        public void _Wear()
        {
            if (_worn) return;
            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
                if (!Networking.IsOwner(gameObject)) return;
            }

            _worn = true;
            _wearer = Networking.LocalPlayer;

            for (var i = 0; i < ownedObjects.Length; i++)
            {
                if (ownedObjects[i] == null || Networking.IsOwner(ownedObjects[i])) continue;
                Networking.SetOwner(Networking.LocalPlayer, ownedObjects[i]);
            }

            if (propRigidbody != null) propRigidbody.isKinematic = true;
            if (disableCollidersWhileWorn) _SetCollidersEnabled(false);

            if (wearEventHandler != null && !string.IsNullOrEmpty(wornEventName))
                wearEventHandler.SendCustomEvent(wornEventName);
        }

        /// <summary>Releases the prop from the wearer.</summary>
        public void _Unwear()
        {
            if (!_worn) return;

            _worn = false;
            _wearer = null;

            if (propRigidbody != null) propRigidbody.isKinematic = false;
            if (disableCollidersWhileWorn) _SetCollidersEnabled(true);

            if (wearEventHandler != null && !string.IsNullOrEmpty(unwornEventName))
                wearEventHandler.SendCustomEvent(unwornEventName);
        }

        public void _ToggleWear()
        {
            if (_worn) _Unwear();
            else _Wear();
        }

        public bool _IsWorn()
        {
            return _worn;
        }

        private void _SetCollidersEnabled(bool enabled)
        {
            for (var i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null) _colliders[i].enabled = enabled;
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        private void OnValidate()
        {
            if (propPickup == null) propPickup = GetComponent<VRCPickup>();
            if (propRigidbody == null) propRigidbody = GetComponent<Rigidbody>();
        }
#endif
    }
}
