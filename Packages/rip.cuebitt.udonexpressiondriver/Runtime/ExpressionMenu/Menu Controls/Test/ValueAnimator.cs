using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ValueAnimator : UdonSharpBehaviour
    {
        [Header("Configuration")]
        [SerializeField] [Range(0f, 10f)] private float speed = 0.5f;
        [SerializeField] private bool animateValues = true;

        [Header("Internal")]
        [SerializeField] private RadialPuppet radialPuppet;
        [SerializeField] private AxisPuppet twoAxisPuppet;
        [SerializeField] private AxisPuppet fourAxisPuppet;

        public void Update()
        {
            if (!animateValues) return;

            var t = Time.time * speed;

            if (radialPuppet != null) radialPuppet.Value = Mathf.SmoothStep(0, 1, Mathf.PingPong(t, 1f));
            if (twoAxisPuppet != null)
                twoAxisPuppet.PuppetValue = new Vector2(0.5f + Mathf.Cos(t) * 0.5f, 0.5f + Mathf.Sin(t) * 0.5f);
            if (fourAxisPuppet != null)
                fourAxisPuppet.PuppetValue = new Vector2(0.5f + Mathf.Cos(t) * 0.5f, 0.5f - Mathf.Sin(t) * 0.5f);
        }
    }
}