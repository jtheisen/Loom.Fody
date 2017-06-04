using System;
using System.ComponentModel;
using System.Diagnostics;

namespace AssemblyToProcess
{
    #region The interfaces and attributes we control the weaving with.

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
        Value Get(Container self, ref MixIn mixIn);
        void Set(Container self, ref MixIn mixIn, Value value);
    }

    public interface IPreviousPropertyImplementation<Value, Container>
    {
        String GetPropertyName();
        Value Get(Container container);
        void Set(Container container, Value value);
    }

    #endregion

    #region The sample implementation: Implementing INotifyPropertyChanged!

    /// <summary>
    /// Our property implementations need a common mix in that implements the event.
    /// </summary>
    public struct MyMixIn<Container> : INotifyPropertyChanged 
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public void Fire(Container self, String propertyName)
        {
            Debug.WriteLine("fired!");

            PropertyChanged?.Invoke(self, new PropertyChangedEventArgs(propertyName));

        }


    }

    /// <summary>
    /// We have an example property implementation that will fire a PropertyChanged event on setting.
    /// </summary>
    /// <typeparam name="Value">The type of the property (Decimal or Int32 in our sample).</typeparam>
    /// <typeparam name="Container">The type of the object we have the properties on.</typeparam>
    /// <typeparam name="OriginalImplementation">Made by the weaver to allow for access to the previous properties.</typeparam>
    public struct MyPropertyImplementation<Value, Container, OriginalImplementation>
        : IPropertyImplementation<IComparable<Value>, Object, Value, Container, MyMixIn<Container>>

        where Value : IComparable<Value>
        where OriginalImplementation : struct, IPreviousPropertyImplementation<Value, Container>
    {
        public OriginalImplementation originalImplementation;

        public Value Get(Container self, ref MyMixIn<Container> mixIn)
        {
            return originalImplementation.Get(self);
        }

        public void Set(Container self, ref MyMixIn<Container> mixIn, Value value)
        {
            originalImplementation.Set(self, value);
            mixIn.Fire(self, originalImplementation.GetPropertyName());
        }
    }

    /// <summary>
    /// This is our sample class to be re-woven. It can actually really live in the
    /// same assembly as the property implementation above.
    /// </summary>
    [Loom(typeof(MyMixIn<>), typeof(MyPropertyImplementation<,,>))]
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
                Decimal = Int32;
            }
        }

        public Decimal Decimal { get; set; }
    }

    #endregion
}
