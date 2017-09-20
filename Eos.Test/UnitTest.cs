using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Eos;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Eos.Test
{
    [TestClass]
    public class SyncTests
    {
        private EosConsole _console = new EosConsole();
        [TestInitialize]
        public void Initialize()
        {
            _console.Connect("192.168.1.160");
            Task.Delay(100).Wait();
            Assert.IsTrue(_console.IsRunning(), "EosSyncLib is not running");
            Assert.IsTrue(_console.IsConnected(), "EosSyncLib is not connected");
            while (!_console.IsSynced()) ;
        }
        [TestMethod]
        public void ConnectionTest()
        {
            Debug.WriteLine(_console.GetCmdLine());
        }
        [TestCleanup]
        public void Cleanup()
        {
            _console.Disconnect();
        }
    }
}
