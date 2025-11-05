using UdonSharp;
using UnityEngine;

namespace UdonExpressionDriver
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ValueAnimator : UdonSharpBehaviour
    {
        [Header("Configuration")]
        [SerializeField, Range(0f, 10f)] private float speed = 1f;
        [SerializeField] private bool animateValues = true;
    
        [Header("Internal")]
        [SerializeField] private RadialPuppet radialPuppet;
        [SerializeField] private AxisPuppet twoAxisPuppet;
        [SerializeField] private AxisPuppet fourAxisPuppet;
    
        public void Update()
        {
            if (!animateValues) return;
        
            var t = Mathf.PingPong(Time.time * speed, 1f);
            var newValue =  Mathf.SmoothStep(0f, 1f, t);
        
            if(radialPuppet != null) radialPuppet.Value = newValue;
            if (twoAxisPuppet != null)
            {
                twoAxisPuppet.PuppetValue = new Vector2(newValue, 1 - newValue);
            }
            if (fourAxisPuppet != null) 
            {
                fourAxisPuppet.PuppetValue = new Vector2(newValue, newValue);
            }
        }
    }
}
