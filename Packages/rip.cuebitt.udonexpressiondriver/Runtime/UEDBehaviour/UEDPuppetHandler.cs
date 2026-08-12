using UdonSharp;

namespace UdonExpressionDriver
{
    /// <summary>
    /// Contract for components that receive value changes from the world-space puppet
    /// controls (RadialPuppet / AxisPuppet). Puppets hold a typed reference to this base
    /// and call the methods directly, so no string-named events or [NetworkCallable]
    /// wiring is needed. Concrete handlers override the methods they care about.
    /// </summary>
    public abstract class UEDPuppetHandler : UdonSharpBehaviour
    {
        /// <summary>Called when a radial puppet's value changes (0..1).</summary>
        public abstract void _OnPuppetRadial(float value);

        /// <summary>Called when a two-axis puppet's x/y change (-1..1 each).</summary>
        public abstract void _OnPuppetTwo(float x, float y);

        /// <summary>Called when a four-axis puppet's directionals change (0..1 each).</summary>
        public abstract void _OnPuppetFour(float negX, float posX, float negY, float posY);

        /// <summary>Called when a puppet control's header is clicked (close/back).</summary>
        public abstract void _OnPuppetClose();
    }
}
