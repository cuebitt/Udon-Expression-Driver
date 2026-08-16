using UdonSharp;

namespace UdonExpressionDriver
{
    /// <summary>
    /// Contract for components that drive a RadialMenu: the menu forwards every wedge press to
    /// <see cref="_OnControlPressed"/>, and concrete hosts (UEDFullController, or a standalone
    /// demo driver) implement their own menu content and navigation. Kept separate from
    /// UEDFullController so the menu can be driven without a full controller/prop.
    /// </summary>
    public abstract class UEDMenuHost : UEDBehaviour
    {
        public abstract void _OnControlPressed(int controlIndex);
    }
}
