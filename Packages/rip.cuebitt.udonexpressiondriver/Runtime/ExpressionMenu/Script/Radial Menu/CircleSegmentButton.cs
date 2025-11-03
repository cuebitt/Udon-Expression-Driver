using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CircleSegmentButton : UdonSharpBehaviour
    {
        public int segmentIndex;
        
        public override void Interact()
        {
            // Todo trigger some event here
            Debug.Log($"Clicked segment {segmentIndex}");
        }
    }
}