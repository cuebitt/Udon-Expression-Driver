using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
#endif

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RadialPuppet : UdonSharpBehaviour
    {
        [Header("Content")]

        [SerializeField, Range(0, 1)] [Tooltip("Current value from 0 to 1")]
        private float value;

        [SerializeField] [Tooltip("Display label for the header")]
        private string label = "Radial Puppet";

        [Header("Event Handler")]

        [SerializeField] [Tooltip("Component to notify when the value changes or the header is clicked")]
        private UEDPuppetHandler handler;

        [SerializeField, HideInInspector] private bool autoLinked;

        [Header("Internal")]

        [SerializeField] private Slider radialSlider;
        [SerializeField] private Slider lowerSlider;
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private TMP_Text valueLabel;

        public float Value
        {
            get => value;
            set
            {
                this.value = value;

                if (valueLabel != null) valueLabel.text = $"{this.value * 100:F0}%";
                if (radialSlider != null) radialSlider.value = this.value;
                if (lowerSlider != null) lowerSlider.value = this.value;
            }
        }

        public string Label
        {
            get => label;
            set
            {
                label = value;

                if (headerLabel != null) headerLabel.text = value;
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void OnValidate()
        {
            Value = value;
            Label = label;

            if (lowerSlider != null)
                EditorApplication.delayCall += () => { if (this == null) return; lowerSlider.SetValueWithoutNotify(value); };
        }
#endif

        public void OnSliderValueChanged()
        {
            Value = lowerSlider.value;

            if (handler != null) handler._OnPuppetRadial(Value);
        }

        public void OnHeaderClicked()
        {
            if (handler != null) handler._OnPuppetClose();
        }
    }
}
