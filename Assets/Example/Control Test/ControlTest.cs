using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace UdonExpressionDriver
{
    // Test harness that prints the puppet control values into TMP text so the
    // controls can be verified without a full menu setup.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ControlTest : UEDPuppetHandler
    {
        [Header("Internal")] [SerializeField] private TMP_Text radialPuppetValue;
        [SerializeField] private TMP_Text twoAxisX;
        [SerializeField] private TMP_Text twoAxisY;
        [SerializeField] private TMP_Text fourAxisNegX;
        [SerializeField] private TMP_Text fourAxisPosX;
        [SerializeField] private TMP_Text fourAxisNegY;
        [SerializeField] private TMP_Text fourAxisPosY;
        [SerializeField] private TMP_Text gestureLeft;
        [SerializeField] private TMP_Text gestureRight;
        
        [Header("Event Handler")]
        [SerializeField] private UdonBehaviour eventHandler;

        public override void _OnPuppetRadial(float value)
        {
            if (radialPuppetValue != null) radialPuppetValue.text = $"{value * 100:F0}%";
        }

        public override void _OnPuppetTwo(float x, float y)
        {
            if (twoAxisX != null) twoAxisX.text = x.ToString("F2");
            if (twoAxisY != null) twoAxisY.text = y.ToString("F2");
        }

        public override void _OnPuppetFour(float negX, float posX, float negY, float posY)
        {
            if (fourAxisNegX != null) fourAxisNegX.text = negX.ToString("F2");
            if (fourAxisPosX != null) fourAxisPosX.text = posX.ToString("F2");
            if (fourAxisNegY != null) fourAxisNegY.text = negY.ToString("F2");
            if (fourAxisPosY != null) fourAxisPosY.text = posY.ToString("F2");
        }

        public override void _OnPuppetClose()
        {
        }

        public override void _OnHandGesture(int left, int right)
        {
            var gestureNames = new[]
                { "Neutral", "Fist", "HandOpen", "FingerPoint", "Victory", "RockNRoll", "HandGun", "ThumbsUp" };
            if (gestureLeft != null) gestureLeft.text = gestureNames[left];
            if (gestureRight != null) gestureRight.text = gestureNames[right];
        }

        public void _HandleRadialPuppetHeaderClick()
        {
            if (eventHandler != null) eventHandler.SendCustomEvent("ToggleAnimateRadialPuppet");
        }

        public void _HandleTwoAxisPuppetHeaderClick()
        {
            if (eventHandler != null) eventHandler.SendCustomEvent("ToggleAnimateTwoAxisPuppet");
        }

        public void _HandleFourAxisPuppetHeaderClick()
        {
            if (eventHandler != null) eventHandler.SendCustomEvent("ToggleAnimateFourAxisPuppet");
        }
    }
}