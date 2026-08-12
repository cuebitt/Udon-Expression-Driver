using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    // Sits on each radial wedge and routes an Interact press to the menu with its
    // segment index, so wedges work as world-space buttons.
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