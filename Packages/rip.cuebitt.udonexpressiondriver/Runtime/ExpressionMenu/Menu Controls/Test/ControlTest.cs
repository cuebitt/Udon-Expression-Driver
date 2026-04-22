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
        [Header("Internal")]
        [SerializeField] private TMP_Text radialPuppetValue;
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
            twoAxisX.text = xValue.ToString("F2");
            twoAxisY.text = yValue.ToString("F2");
        }

        [NetworkCallable]
        public void OnFourAxisValueChanged(float negXValue, float posXValue, float negYValue, float posYValue)
        {
            fourAxisNegX.text = negXValue.ToString("F2");
            fourAxisPosX.text = posXValue.ToString("F2");
            fourAxisNegY.text = negYValue.ToString("F2");
            fourAxisPosY.text = posYValue.ToString("F2");
        }
    }
}