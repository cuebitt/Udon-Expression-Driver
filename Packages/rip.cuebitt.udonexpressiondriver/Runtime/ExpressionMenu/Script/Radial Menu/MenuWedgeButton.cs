using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MenuWedgeButton : UdonSharpBehaviour
    {
        public int segmentIndex;
        
        public override void Interact()
        {
            // Todo trigger some event here
            Debug.Log($"Clicked segment {segmentIndex}");
        }
    }
}