using System.Collections.Generic;

namespace UdonExpressionDriver.Editor.Templates
{
    public enum UEDParameterType
    {
        Int = 0,
        Float = 1,
        Bool = 2
    }

    public class UEDParameter
    {
        public string name;
        public bool saved;
        public bool networkSynced;
        public UEDParameterType type;
        public object defaultValue;
    }

    public class UEDControl
    {
        
    }

    public partial class UEDDriverTemplate
    {
        // Meta
        public string ClassName;
    
        // Parameters
        public IList<UEDParameter> Parameters;
        public IList<UEDControl> Controls;
    }
}