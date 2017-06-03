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

            instance.Int32 = 42;

            Assert.IsTrue(hadEvent);
        }
    }
}
