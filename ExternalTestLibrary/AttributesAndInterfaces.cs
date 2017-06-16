using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//
// The interfaces and attributes we control the weaving with.
// 

namespace AssemblyToProcess
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public class LoomAttribute : Attribute
    {
        public LoomAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class WeaveClassAttribute : Attribute
    {
        public WeaveClassAttribute(Type mixIn, Type propertyImplementation)
        {
            MixIn = mixIn;
            PropertyImplementation = propertyImplementation;
        }

        public Type MixIn { get; }
        public Type PropertyImplementation { get; }
    }

    public class ReferenceAttribute
    {
        public ReferenceAttribute(String name)
        {

        }
    }


    [AttributeUsage(AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
    public class WeaveEventDelegationAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class WeaveMethodDelegationAttribute : Attribute
    {
    }

    public interface IPropertyImplementation
        <ValueInterface, ContainerInterface, Value, Container, MixIn, Accessor>
        where Value : ValueInterface
        where Container : ContainerInterface
        where MixIn : struct
        where Accessor : IAccessor<Value, Container>
    {
        Value Get(
            Container self,
            ref MixIn mixIn
            );

        void Set(
            Container self,
            ref MixIn mixIn,
            Value value
            );
    }

    public interface IAccessor<Value, Container>
    {
        String GetPropertyName();
        Int32 GetIndex();
        Boolean IsVariable();

        Value Get(Container container);
        void Set(Container container, Value value);
    }
}
