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

            Assert.ThrowsException<NotImplementedException>(() => withDelegationMethods.DelegateMe(-1, "test"));
        }

        [TestMethod]
        public void CheckDelegationToProperty()
        {
            var instance = new ClassToHaveItsPropertiesModified();

            var withDelegationMethods = instance as IWithDelegationMethods;

            Assert.ThrowsException<NotImplementedException>(() => withDelegationMethods.DelegateMe(0, "test"));
        }
    }
}
