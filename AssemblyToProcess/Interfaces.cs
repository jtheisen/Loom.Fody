using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AssemblyToProcess
{
    // The interfaces and attributes we control the weaving with.

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class LoomAttribute : Attribute
    {
        public LoomAttribute(Type mixIn, Type propertyImplementation)
        {
            MixIn = mixIn;
            PropertyImplementation = propertyImplementation;
        }

        public Type MixIn { get; }
        public Type PropertyImplementation { get; }
    }

    // See sample below for an explanation of the type parameters.

    public interface IPropertyImplementation<ValueInterface, ContainerInterface, Value, Container, MixIn>
        where Value : ValueInterface
        where Container : ContainerInterface
        where MixIn : struct
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

    public interface IPreviousPropertyImplementation<Value, Container>
    {
        String GetPropertyName();
        Int32 GetIndex();

        Value Get(Container container);
        void Set(Container container, Value value);
    }
}
