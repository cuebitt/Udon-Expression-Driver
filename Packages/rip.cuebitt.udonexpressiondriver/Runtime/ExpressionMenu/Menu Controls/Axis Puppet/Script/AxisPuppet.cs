using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

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
        
        [SerializeField] [Tooltip("Display label for the header")]
        private string label = "Axis Puppet";

        [SerializeField] private AxisPuppetType axisPuppetType = AxisPuppetType.Four;

        [SerializeField] [Tooltip("2-axis: [X, Y]; 4-axis: [-X, +X, -Y, +Y]")]
        private string[] axisLabels = { "-X", "+X", "-Y", "+Y" };

        [SerializeField]
        private Vector2 puppetValue = new Vector2(0.5f, 0.5f);

        [Header("Event Handler")]
        
        [SerializeField] [Tooltip("Component to notify when the value changes or the header is clicked")]
        private UEDPuppetHandler handler;

        [SerializeField, HideInInspector] private bool autoLinked;

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

        // Always normalize to 4 slots so index math below is safe. A 2-axis puppet only
        // shows the first two and clears the others, so no stale text lingers after switching.
        public string[] AxisLabels
        {
            get => axisLabels;
            set
            {
                var copy = new string[4];
                if (value != null)
                {
                    for (var i = 0; i < 4 && i < value.Length; i++)
                        copy[i] = value[i];
                }
                axisLabels = copy;

                if (leftAxisLabel != null) leftAxisLabel.text = axisLabels[0];
                if (rightAxisLabel != null) rightAxisLabel.text = axisLabels[1];
                if (bottomAxisLabel != null) bottomAxisLabel.text = axisLabels[2];
                if (topAxisLabel != null) topAxisLabel.text = axisLabels[3];

                if (AxisPuppetType == AxisPuppetType.Two)
                {
                    if (leftAxisLabel != null) leftAxisLabel.text = "";
                    if (bottomAxisLabel != null) bottomAxisLabel.text = "";
                }
            }
        }

        public Vector2 PuppetValue
        {
            get => puppetValue;
            set
            {
                // Clamp once and use the clamped value everywhere so the pointer and
                // the sliders always agree (the pointer was using the raw input before).
                var pv = new Vector2(Mathf.Clamp(value.x, 0f, 1f), Mathf.Clamp(value.y, 0f, 1f));
                puppetValue = pv;

                _PositionPointer(pv, _valuePanelSize);

                if (xAxisSlider != null) xAxisSlider.value = pv.x;
                if (yAxisSlider != null) yAxisSlider.value = pv.y;
            }
        }

        private void Start()
        {
            if (valuePanel != null) _valuePanelSize = valuePanel.sizeDelta;

            // Re-apply: the controller seeds PuppetValue when opening the panel, which can run
            // before Start (the GameObject is activated and seeded in the same frame), so the
            // pointer may still be sitting at the panel center from the zero cached size.
            _PositionPointer(puppetValue, _valuePanelSize);
        }

        // Moves the value pointer inside the panel. Panel size comes from _valuePanelSize at
        // runtime and straight from sizeDelta in OnValidate (before Start has cached it).
        private void _PositionPointer(Vector2 value, Vector2 panelSize)
        {
            if (valuePointer == null) return;

            var newPos = new Vector3(panelSize.x * value.x, panelSize.y * value.y, 0f);
            newPos -= new Vector3(panelSize.x * 0.5f, panelSize.y * 0.5f, 0f);
            valuePointer.localPosition = newPos;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void OnValidate()
        {
            Label = label;
            AxisLabels = axisLabels;

            if (valuePanel != null)
                _PositionPointer(puppetValue, valuePanel.sizeDelta);

            // Setting .value directly fires OnValueChanged (which re-enters this behaviour),
            // so push slider updates to the next frame via SetValueWithoutNotify.
            if (xAxisSlider != null)
                EditorApplication.delayCall += () => { if (this == null) return; xAxisSlider.SetValueWithoutNotify(puppetValue.x); };
            if (yAxisSlider != null)
                EditorApplication.delayCall += () => { if (this == null) return; yAxisSlider.SetValueWithoutNotify(puppetValue.y); };
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

        public void OnHeaderClicked()
        {
            if (handler != null) handler._OnPuppetClose();
        }

        // Converts the panel coords (0..1) into the directional values the handler expects:
        // two-axis sends x/y as -1..1, four-axis sends each direction's strength as 0..1.
        private void SendValueUpdate()
        {
            if (handler == null) return;

            if (AxisPuppetType == AxisPuppetType.Four)
            {
                var coords = PuppetValue;

                // X direction
                var dxPlus  = Mathf.Max(coords.x * 2 - 1, 0f);
                var dxMinus = Mathf.Max(1 - coords.x * 2, 0f);

                // Y direction
                var dyPlus  = Mathf.Max(coords.y * 2 - 1, 0f);
                var dyMinus = Mathf.Max(1 - coords.y * 2, 0f);

                handler._OnPuppetFour(dxMinus, dxPlus, dyMinus, dyPlus);
            }
            else if (AxisPuppetType == AxisPuppetType.Two)
            {
                var xValue = PuppetValue.x * 2 - 1;
                var yValue = PuppetValue.y * 2 - 1;

                handler._OnPuppetTwo(xValue, yValue);
            }
        }
    }
}
