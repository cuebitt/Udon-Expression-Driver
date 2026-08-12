using UdonSharp;

namespace UdonExpressionDriver
{
    /// <summary>
    /// Base class shared by UED prop behaviours. It does nothing on its own and ignores
    /// puppet handler callbacks by default; subclasses override them when needed.
    /// </summary>
    public class UEDBehaviour : UEDPuppetHandler
    {
        public override void _OnPuppetRadial(float value)
        {
        }

        public override void _OnPuppetTwo(float x, float y)
        {
        }

        public override void _OnPuppetFour(float negX, float posX, float negY, float posY)
        {
        }

        public override void _OnPuppetClose()
        {
        }
    }
}
