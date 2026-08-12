using UdonSharp;
using UnityEngine;
using VRC.Dynamics;

namespace UdonExpressionDriver
{
    /// <summary>
    /// Sits on a GameObject with a VRC Contact Sender or Receiver and forwards
    /// contact events to a target behaviour via SendCustomEvent.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ContactForwarder : UdonSharpBehaviour
    {
        [Header("Target")]
        [Tooltip("Behavior that receives the forwarded events.")]
        [SerializeField] private UdonSharpBehaviour target;
        [Tooltip("Event sent to the target when a contact enters.")]
        [SerializeField] private string enterEventName;
        [Tooltip("Event sent to the target when a contact exits.")]
        [SerializeField] private string exitEventName;

        [SerializeField, HideInInspector] private bool autoLinked;

        public override void OnContactEnter(ContactEnterInfo contactInfo)
        {
            _Send(enterEventName);
        }

        public override void OnContactExit(ContactExitInfo contactInfo)
        {
            _Send(exitEventName);
        }

        private void _Send(string eventName)
        {
            if (target != null && !string.IsNullOrEmpty(eventName))
                target.SendCustomEvent(eventName);
        }
    }
}
