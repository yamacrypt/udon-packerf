using System;

namespace UdonPacker{
    [AttributeUsage(AttributeTargets.Field)]
    public class PackingAttribute : Attribute
    {

    }
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public class InterfaceToClassAttribute : Attribute
    {
    public string InterfaceName;
    public string ClassName;
        public InterfaceToClassAttribute(string interfaceName,string className)
        {
            this.InterfaceName=interfaceName;
            this.ClassName=className;
        }

    
    }
}