using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AssemblyToProcess;
using System.ComponentModel;

namespace Loom.Tests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            var instance = new ClassToHaveItsPropertiesModified();

            var hadEvent = false;

            (instance as INotifyPropertyChanged).PropertyChanged += (o, e) =>
            {
                hadEvent = true;
            };

            var dummy2 = instance.Int32;
            var dummy1 = instance.Decimal;

            instance.Int32 = 42;

            Assert.IsTrue(hadEvent);
        }

        [TestMethod]
        public void CheckDelegationToMixIn()
        {
            var instance = new ClassToHaveItsPropertiesModified();

            var withDelegationMethods = instance as IWithDelegationMethods;

            Assert.ThrowsException<NotImplementedException>(() => withDelegationMethods.GetPropertyValue(-1));
        }

        [TestMethod]
        public void CheckDelegationToProperty()
        {
            var instance = new ClassToHaveItsPropertiesModified();

            var withDelegationMethods = instance as IWithDelegationMethods;

            instance.Int32 = 42;

            Assert.AreEqual(42, withDelegationMethods.GetPropertyValue(0));
            Assert.AreEqual(42m, withDelegationMethods.GetPropertyValue(1));

            withDelegationMethods.SetPropertyValue(0, 43);

            Assert.AreEqual(43m, withDelegationMethods.GetPropertyValue(1));

            withDelegationMethods.SetPropertyValue(1, 44m);

            Assert.AreEqual(44, withDelegationMethods.GetPropertyValue(0));

            Assert.ThrowsException<InvalidCastException>
                (() => withDelegationMethods.SetPropertyValue(1, "no!"));
        }
    }
}
