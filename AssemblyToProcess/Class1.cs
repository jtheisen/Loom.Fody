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

    #endregion

    #region The sample implementation: Implementing INotifyPropertyChanged!

    public interface IWithDelegationMethods
    {
        Object GetPropertyValue(Int32 index);
        void SetPropertyValue(Int32 index, Object value);
    }

    /// <summary>
    /// Our property implementations need a common mix in that implements the event.
    /// </summary>
    public struct MyMixIn<Container> : IWithDelegationMethods, INotifyPropertyChanged 
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public Object GetPropertyValue(Int32 index) => throw new NotImplementedException();
        public void SetPropertyValue(Int32 index, Object value) => throw new NotImplementedException();

        public void Fire(Container self, String propertyName)
        {
            PropertyChanged?.Invoke(self, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// We have an example property implementation that will fire a PropertyChanged event on setting.
    /// </summary>
    /// <typeparam name="Value">The type of the property (Decimal or Int32 in our sample).</typeparam>
    /// <typeparam name="Container">The type of the object we have the properties on.</typeparam>
    /// <typeparam name="PreviousImplementation">Made by the weaver to allow for access to the previous properties.</typeparam>
    public struct MyPropertyImplementation<Value, Container, PreviousImplementation>
        : IPropertyImplementation<IComparable<Value>, Object, Value, Container, MyMixIn<Container>>

        where Value : IComparable<Value>
        where PreviousImplementation : struct, IPreviousPropertyImplementation<Value, Container>
    {
        PreviousImplementation previous;

        public Object GetPropertyValue(Container self, ref MyMixIn<Container> mixIn)
            => Get(self, ref mixIn);

        public void SetPropertyValue(Container self, ref MyMixIn<Container> mixIn, Object value)
            => Set(self, ref mixIn, (Value)value);

        public Value Get(Container self, ref MyMixIn<Container> mixIn)
        {
            return previous.Get(self);
        }

        public void Set(Container self, ref MyMixIn<Container> mixIn, Value value)
        {
            previous.Set(self, value);
            mixIn.Fire(self, previous.GetPropertyName());
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
                Decimal = value;
            }
        }

        public Decimal Decimal { get; set; }
    }

    #endregion

    #region Playground

    public class Playground
    {
        public int Foo(int i)
        {
            switch (i)
            {
                case 1: return Bar1();
                case 2: return Bar2();
                case 3: return Bar3();
                case 4: return Bar4();
                case 5: return Bar5();
                case 6: return Bar6();
                case 7: return Bar7();
                default: throw new NotImplementedException($"No such property index {i} on this object {this}.");
            }
        }

        public int Bar1() { return 0; }
        public int Bar2() { return 0; }
        public int Bar3() { return 0; }
        public int Bar4() { return 0; }
        public int Bar5() { return 0; }
        public int Bar6() { return 0; }
        public int Bar7() { return 0; }
    }

    #endregion
}
