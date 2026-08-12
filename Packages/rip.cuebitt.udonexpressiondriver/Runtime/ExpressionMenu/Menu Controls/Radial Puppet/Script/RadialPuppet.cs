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

        [SerializeField] [FieldChangeCallback(nameof(Value))] [Range(0, 1)] [Tooltip("Current value from 0 to 1")]
        private float value;

        [SerializeField] [FieldChangeCallback(nameof(Label))] [Tooltip("Display label for the header")]
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
                // TODO: assigning .value fires the wired OnValueChanged (a spurious _OnPuppetRadial
                // callback on programmatic sets). Switch to SetValueWithoutNotify once its Udon
                // exposure is confirmed via Tools > UED > Dump Udon Exposure.
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
