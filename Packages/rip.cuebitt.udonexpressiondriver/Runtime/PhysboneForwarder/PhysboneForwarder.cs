using UdonSharp;
using UnityEngine;
using VRC.Dynamics;

namespace UdonExpressionDriver
{
    /// <summary>
    /// Sits on a GameObject with a VRCPhysBone and forwards PhysBone events to a
    /// target behaviour via SendCustomEvent. Leave an event name empty to ignore it.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PhysboneForwarder : UdonSharpBehaviour
    {
        [Header("Target")]
        [Tooltip("Behavior that receives the forwarded events.")]
        [SerializeField] private UdonSharpBehaviour target;
        [Tooltip("Event sent to the target when the bone chain is grabbed.")]
        [SerializeField] private string grabbedEventName;
        [Tooltip("Event sent to the target when the bone chain is released.")]
        [SerializeField] private string releasedEventName;
        [Tooltip("Event sent to the target when the bone chain is posed.")]
        [SerializeField] private string posedEventName;
        [Tooltip("Event sent to the target when the bone chain is unposed.")]
        [SerializeField] private string unposedEventName;

        [SerializeField, HideInInspector] private bool autoLinked;

        public override void OnPhysBoneGrabbed(PhysBoneGrabbedInfo physBoneInfo)
        {
            _Send(grabbedEventName);
        }

        public override void OnPhysBoneReleased(PhysBoneReleasedInfo physBoneInfo)
        {
            _Send(releasedEventName);
        }

        public override void OnPhysBonePosed(PhysBonePosedInfo physBoneInfo)
        {
            _Send(posedEventName);
        }

        public override void OnPhysBoneUnPosed(PhysBoneUnPosedInfo physBoneInfo)
        {
            _Send(unposedEventName);
        }

        private void _Send(string eventName)
        {
            if (target != null && !string.IsNullOrEmpty(eventName))
                target.SendCustomEvent(eventName);
        }
    }
}
