using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MenuWedgeButton : UdonSharpBehaviour
    {
        public int segmentIndex;
        public RadialMenu radialMenu;
        
        public override void Interact()
        {
            if (radialMenu != null)
            {
                radialMenu.OnButtonPress(segmentIndex);
            }
        }
    }
}