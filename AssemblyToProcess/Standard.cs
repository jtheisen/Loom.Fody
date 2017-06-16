using System;
using System.ComponentModel;
using System.Diagnostics;

namespace AssemblyToProcess.Standard
{
    /// <summary>
    /// This is our sample class to be re-woven. It can actually really live in the
    /// same assembly as the property implementation above.
    /// </summary>
    [WeaveClass(typeof(MyMixIn<>), typeof(MyPropertyImplementation<,,>))]
    public class ClassToHaveItsPropertiesModified
    {
        public Int32 Int32
        {
            get
            {
                return (Int32)Decimal;
            }
            set
            {
                Decimal = value;
            }
        }

        public Decimal Decimal { get; set; }
    }

    /// <summary>
    /// This is a generic sample class to be re-woven.
    /// </summary>
    [WeaveClass(typeof(MyMixIn<>), typeof(MyPropertyImplementation<,,>))]
    public class GenericClassToHaveItsPropertiesModified<Param>
    {
        public Param Value { get; set; }
    }

}
