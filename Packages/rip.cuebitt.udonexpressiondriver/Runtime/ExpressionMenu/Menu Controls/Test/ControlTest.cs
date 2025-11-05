using System.Globalization;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ControlTest : UdonSharpBehaviour
    {
        [Header("Internal")] [SerializeField] private TMP_Text radialPuppetValue;

        [SerializeField] private TMP_Text twoAxisX;
        [SerializeField] private TMP_Text twoAxisY;

        [SerializeField] private TMP_Text fourAxisNegX;
        [SerializeField] private TMP_Text fourAxisPosX;
        [SerializeField] private TMP_Text fourAxisNegY;
        [SerializeField] private TMP_Text fourAxisPosY;

        [NetworkCallable]
        public void OnRadialPuppetValueChanged(float value)
        {
            radialPuppetValue.text = $"{value * 100:F0}%";
        }

        [NetworkCallable]
        public void OnTwoAxisValueChanged(float xValue, float yValue)
        {
            twoAxisX.text = xValue.ToString(CultureInfo.InvariantCulture);
            twoAxisY.text = yValue.ToString(CultureInfo.InvariantCulture);
        }

        [NetworkCallable]
        public void OnFourAxisValueChanged(float negXValue, float posXValue, float negYValue, float posYValue)
        {
            fourAxisNegX.text = negXValue.ToString(CultureInfo.InvariantCulture);
            fourAxisPosX.text = posXValue.ToString(CultureInfo.InvariantCulture);
            fourAxisNegY.text = negYValue.ToString(CultureInfo.InvariantCulture);
            fourAxisPosY.text = posYValue.ToString(CultureInfo.InvariantCulture);
        }
    }
}