using System;
using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon.Common.Interfaces;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
#endif

namespace UdonExpressionDriver
{
    public enum AxisPuppetType
    {
        Two,
        Four
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AxisPuppet : UdonSharpBehaviour
    {
        [Header("Content")]
        
        [SerializeField] [FieldChangeCallback(nameof(Label))]
        private string label = "Axis Puppet";

        [SerializeField] [FieldChangeCallback(nameof(AxisPuppetType))]
        private AxisPuppetType axisPuppetType = AxisPuppetType.Four;

        [SerializeField] [FieldChangeCallback(nameof(AxisLabels))] [Tooltip("2-axis: [X, Y]; 4-axis: [-X, +X, -Y, +Y]")]
        private string[] axisLabels = { "-X", "+X", "-Y", "+Y" };

        [SerializeField] [FieldChangeCallback(nameof(PuppetValue))]
        private Vector2 puppetValue = new Vector2(0.5f, 0.5f);

        [Header("Event Handler")]
        
        [SerializeField] private UdonSharpBehaviour eventHandlerBehaviour;
        [SerializeField] private string twoAxisEventName;
        [SerializeField] private string fourAxisEventName;

        [Header("Internal")]
        
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private Slider xAxisSlider;
        [SerializeField] private Slider yAxisSlider;
        [SerializeField] private TMP_Text leftAxisLabel;
        [SerializeField] private TMP_Text rightAxisLabel;
        [SerializeField] private TMP_Text topAxisLabel;
        [SerializeField] private TMP_Text bottomAxisLabel;
        [SerializeField] private RectTransform valuePanel;
        [SerializeField] private RectTransform valuePointer;

        private Vector2 _valuePanelSize;

        public string Label
        {
            get => label;
            set
            {
                label = value;

                if (headerLabel != null) headerLabel.text = label;
            }
        }

        public AxisPuppetType AxisPuppetType
        {
            get => axisPuppetType;
            set => axisPuppetType = value;
        }

        public string[] AxisLabels
        {
            get => axisLabels;
            set
            {
                axisLabels = new string[4];
                for (var i = 0; i < 4; i++) axisLabels[i] = ""; // ensure there are always 4 strings in the array

                for (var i = 0; i < Math.Min(value.Length, 4); i++) axisLabels[i] = value[i];

                if (AxisPuppetType == AxisPuppetType.Four)
                {
                    if (leftAxisLabel != null) leftAxisLabel.text = axisLabels[0];
                    if (rightAxisLabel != null) rightAxisLabel.text = axisLabels[1];
                    if (bottomAxisLabel != null) bottomAxisLabel.text = axisLabels[2];
                    if (topAxisLabel != null) topAxisLabel.text = axisLabels[3];
                }
                else
                {
                    if (rightAxisLabel != null) rightAxisLabel.text = axisLabels[0];
                    if (topAxisLabel != null) topAxisLabel.text = axisLabels[1];
                }
            }
        }

        public Vector2 PuppetValue
        {
            get => puppetValue;
            set
            {
                var pv = new Vector2(Mathf.Clamp(value.x, 0f, 1f), Mathf.Clamp(value.y, 0f, 1f));
                puppetValue = pv;
                
                // Set pointer value
                var newPos = new Vector3(_valuePanelSize.x * value.x, _valuePanelSize.y * value.y, 0);
                newPos -= new Vector3(_valuePanelSize.x * 0.5f, _valuePanelSize.y * 0.5f, 0);

                ((RectTransform)valuePointer.transform).localPosition = newPos;
                
                // Set slider values
                xAxisSlider.value = pv.x;
                yAxisSlider.value = pv.y;
            }
        }

        private void Start()
        {
            if (valuePanel != null) _valuePanelSize = valuePanel.sizeDelta;
        }


#if UNITY_EDITOR && !COMPILER_UDONSHARP
        public void OnValidate()
        {
            Label = label;

            var _axisLabels = new string[4];
            for (var i = 0; i < axisLabels.Length; i++) _axisLabels[i] = axisLabels[i];

            if (_axisLabels.Length < 4)
                for (var i = _axisLabels.Length; i < 4; i++)
                    _axisLabels[i] = "";

            if (AxisPuppetType == AxisPuppetType.Four)
            {
                if (leftAxisLabel != null) leftAxisLabel.text = _axisLabels[0];
                if (rightAxisLabel != null) rightAxisLabel.text = _axisLabels[1];
                if (bottomAxisLabel != null) bottomAxisLabel.text = _axisLabels[2];
                if (topAxisLabel != null) topAxisLabel.text = _axisLabels[3];
            }
            else if (AxisPuppetType == AxisPuppetType.Two)
            {
                if (rightAxisLabel != null) rightAxisLabel.text = _axisLabels[0];
                if (topAxisLabel != null) topAxisLabel.text = _axisLabels[1];

                if (leftAxisLabel != null) leftAxisLabel.text = "";
                if (bottomAxisLabel != null) bottomAxisLabel.text = "";
            }

            if (xAxisSlider != null)
                EditorApplication.delayCall += () => { xAxisSlider.SetValueWithoutNotify(PuppetValue.x); };

            if (yAxisSlider != null)
                EditorApplication.delayCall += () => { yAxisSlider.SetValueWithoutNotify(PuppetValue.y); };

            if (valuePanel != null && valuePointer != null)
            {
                var vps = valuePanel.sizeDelta;
                var newPos = new Vector3(vps.x * PuppetValue.x, vps.y * PuppetValue.y, 0);
                newPos -= new Vector3(vps.x * 0.5f, vps.y * 0.5f, 0);

                ((RectTransform)valuePointer.transform).localPosition = newPos;
            }
        }
#endif

        public void OnXSliderValueChanged()
        {
            var newPos = PuppetValue;
            newPos.x = xAxisSlider.value;

            PuppetValue = newPos;

            SendValueUpdate();
        }

        public void OnYSliderValueChanged()
        {
            var newPos = PuppetValue;
            newPos.y = yAxisSlider.value;

            PuppetValue = newPos;

            SendValueUpdate();
        }

        private void SendValueUpdate()
        {
            if (eventHandlerBehaviour == null) return;

            if (AxisPuppetType == AxisPuppetType.Four)
            {
                if (string.IsNullOrEmpty(fourAxisEventName)) return;

                var coords = PuppetValue;
                
                // X direction
                var dxPlus  = Mathf.Max(coords.x * 2 - 1, 0f);
                var dxMinus = Mathf.Max(1 - coords.x * 2, 0f);

                // Y direction
                var dyPlus  = Mathf.Max(coords.y * 2 - 1, 0f);
                var dyMinus = Mathf.Max(1 - coords.y * 2, 0f);

                eventHandlerBehaviour.SendCustomNetworkEvent(NetworkEventTarget.Self, fourAxisEventName, dxMinus,
                    dxPlus, dyMinus, dyPlus);
            }
            else if (AxisPuppetType == AxisPuppetType.Two)
            {
                if (string.IsNullOrEmpty(twoAxisEventName)) return;

                var xValue = PuppetValue.x * 2 - 1;
                var yValue = PuppetValue.y * 2 - 1;

                eventHandlerBehaviour.SendCustomNetworkEvent(NetworkEventTarget.Self, twoAxisEventName, xValue, yValue);
            }
        }

        public static Vector4 ToFourAxis(Vector2 value)
        {
            // X direction
            var dxMinus = Mathf.Max(1 - value.x * 2, 0f);
            var dxPlus  = Mathf.Max(value.x * 2 - 1, 0f);
            
            // Y direction
            var dyMinus = Mathf.Max(1 - value.y * 2, 0f);
            var dyPlus  = Mathf.Max(value.y * 2 - 1, 0f);
            
            return  new Vector4(dxMinus, dxPlus, dyMinus, dyPlus);
        }

        public static Vector2 ToTwoAxis(Vector4 value)
        {
            var xAxis = value.x + (value.x / 2f) + value.y + (value.y / 2f);
            var yAxis = value.z + (value.z / 2f) + value.w + (value.w / 2f);
            
            return new Vector2(xAxis, yAxis);
        }
    }
}