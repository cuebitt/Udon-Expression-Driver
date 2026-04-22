using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using VRC.Udon.Common.Interfaces;

#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UnityEditor;
#endif

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RadialPuppet : UdonSharpBehaviour
    {
        [Header("Content")]

        [SerializeField] [FieldChangeCallback(nameof(Value))] [Range(0, 1)] [Tooltip("Current value from 0 to 1")]
        private float value;

        [SerializeField] [FieldChangeCallback(nameof(Label))] [Tooltip("Display label for the header")]
        private string label = "Radial Puppet";

        [Header("Event Handler")]

        [SerializeField] [Tooltip("Behaviour to send events to")]
        private UdonSharpBehaviour eventHandlerBehaviour;
        [SerializeField] [Tooltip("Event name to fire when value changes")]
        private string valueChangedEventName;
        [SerializeField] [Tooltip("Event name to fire when header is clicked")]
        private string headerClickedEventName;

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

#if UNITY_EDITOR && !COMPILER_UDONSHARP
        public void OnValidate()
        {
            Value = value;
            Label = label;

            if (lowerSlider != null)
                EditorApplication.delayCall += () => { lowerSlider.SetValueWithoutNotify(value); };
        }
#endif
        
        public void OnSliderValueChanged()
        {
            Value = lowerSlider.value;

            if (eventHandlerBehaviour != null && !string.IsNullOrEmpty(valueChangedEventName))
                eventHandlerBehaviour.SendCustomNetworkEvent(NetworkEventTarget.Self, valueChangedEventName, Value);
        }

        public void OnHeaderClicked()
        {
            if (eventHandlerBehaviour != null && !string.IsNullOrEmpty(headerClickedEventName))
                eventHandlerBehaviour.SendCustomEvent(headerClickedEventName);
        }
    }
}