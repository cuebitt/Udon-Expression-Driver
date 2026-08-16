using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    // Test helper that drives the puppet controls on a loop so their visuals can be
    // checked in ClientSim without a hand on the sliders.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ValueAnimator : UdonSharpBehaviour
    {
        [Header("Configuration")]
        [SerializeField] [Range(0f, 10f)] private float speed = 0.5f;
        [SerializeField] private bool animateValues = true;
        [SerializeField] private bool animateRadialPuppet = true;
        [SerializeField] private bool animateTwoAxisPuppet = true;
        [SerializeField] private bool animateFourAxisPuppet = true;
        
        [Header("Internal")]
        [SerializeField] private RadialPuppet radialPuppet;
        [SerializeField] private AxisPuppet twoAxisPuppet;
        [SerializeField] private AxisPuppet fourAxisPuppet;

        public void Update()
        {
            if (!animateValues) return;

            var t = Time.time * speed;

            if (animateRadialPuppet && radialPuppet != null) radialPuppet.Value = Mathf.SmoothStep(0, 1, Mathf.PingPong(t, 1f));
            if (animateTwoAxisPuppet && twoAxisPuppet != null)
                twoAxisPuppet.PuppetValue = new Vector2(0.5f + Mathf.Cos(t) * 0.5f, 0.5f + Mathf.Sin(t) * 0.5f);
            if (animateFourAxisPuppet && fourAxisPuppet != null)
                fourAxisPuppet.PuppetValue = new Vector2(0.5f + Mathf.Cos(t) * 0.5f, 0.5f - Mathf.Sin(t) * 0.5f);
        }
        
        public void ToggleAnimateValues()
        {
            animateValues = !animateValues;
        }

        public void ToggleAnimateRadialPuppet()
        {
            animateRadialPuppet = !animateRadialPuppet;
        }

        public void ToggleAnimateTwoAxisPuppet()
        {
            animateTwoAxisPuppet = !animateTwoAxisPuppet;
        }

        public void ToggleAnimateFourAxisPuppet()
        {
            animateFourAxisPuppet = !animateFourAxisPuppet;
        }
    }
}