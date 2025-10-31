using System.Collections.Generic;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace UdonExpressionDriver.Editor.Templates
{
    public partial class UEDDriverTemplate
    {
        // Meta
        public string ClassName;
    
        // Parameters
        public IList<VRCExpressionParameters.Parameter> Parameters;
        public IList<VRCExpressionsMenu.Control> Controls;
    }
}