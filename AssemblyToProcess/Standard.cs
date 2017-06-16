using IronStone.Loom;
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace AssemblyToProcess.Standard
{
    // The sample implementation: Implementing INotifyPropertyChanged!

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
        [WeaveEventDelegation]
        public event PropertyChangedEventHandler PropertyChanged;

        [WeaveMethodDelegation]
        public Object GetPropertyValue(Int32 index) => throw new NotImplementedException();
        [WeaveMethodDelegation]
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

    // This can be used in place of the WeaveClassAttribute that is on it.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    [WeaveClass(typeof(MyMixIn<>), typeof(MyPropertyImplementation<,,>))]
    public class WeaveAsMyPropertyImplementationAttribute : Attribute
    {
    }

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
