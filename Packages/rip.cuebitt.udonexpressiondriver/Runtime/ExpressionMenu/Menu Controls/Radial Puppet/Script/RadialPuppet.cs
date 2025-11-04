using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Udon.Common.Interfaces;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RadialPuppet : UdonSharpBehaviour
    {
        [Header("Content")]
        [SerializeField, FieldChangeCallback(nameof(Value)), Range(0, 1)]
        private float value;

        public float Value
        {
            get => value;
            set
            {
                this.value = value;

                if (valueLabel != null) valueLabel.text = $"{this.value * 100:F0}%";
                if (radialSlider != null) radialSlider.value = this.value;
            }
        }

        [SerializeField, FieldChangeCallback(nameof(Label))]
        private string label = "Radial Puppet";

        public string Label
        {
            get => label;
            set
            {
                label = value;

                if (headerLabel != null) headerLabel.text = label;
            }
        }

        [Header("Event Handler")] [SerializeField]
        private UdonSharpBehaviour eventHandlerBehaviour;

        [SerializeField] private string eventName;

        [Header("Internal")] [SerializeField] private Slider radialSlider;
        [SerializeField] private Slider lowerSlider;
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private TMP_Text valueLabel;


        public void OnSliderValueChanged()
        {
            Value = lowerSlider.value;

            if (eventHandlerBehaviour != null && !string.IsNullOrEmpty(eventName))
            {
                SendCustomNetworkEvent(NetworkEventTarget.Self, eventName, Value);
            }
        }

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        public void OnValidate()
        {
            Value = this.value;
            Label = this.label;

            if (lowerSlider != null)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    lowerSlider.SetValueWithoutNotify(this.value);
                };
            }
        }
#endif
    }
}