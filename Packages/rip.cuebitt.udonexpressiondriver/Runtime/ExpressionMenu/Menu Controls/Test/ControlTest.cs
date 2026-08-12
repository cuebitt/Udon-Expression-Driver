using TMPro;
using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    // Test harness that prints the puppet control values into TMP text so the
    // controls can be verified without a full menu setup.
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ControlTest : UEDPuppetHandler
    {
        [Header("Internal")]
        [SerializeField] private TMP_Text radialPuppetValue;
        [SerializeField] private TMP_Text twoAxisX;
        [SerializeField] private TMP_Text twoAxisY;
        [SerializeField] private TMP_Text fourAxisNegX;
        [SerializeField] private TMP_Text fourAxisPosX;
        [SerializeField] private TMP_Text fourAxisNegY;
        [SerializeField] private TMP_Text fourAxisPosY;

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
    }
}
