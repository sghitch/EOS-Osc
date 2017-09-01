using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Eos.Osc;
using System.Net;
using System.Threading;

namespace Eos.Osc.Test
{
    [TestClass]
    public class FunctionalTests
    {
        private OSCClient client;
        private int port = 3032;
        private IPAddress ip = IPAddress.Parse("192.168.1.113");

        [TestInitialize]
        public void Initalize()
        {
            client = new OSCClient(ip, port);
            client.Connect();
            Assert.IsTrue(client.Connected, "Client was unable to connect");
            Thread.Sleep(5000);
        }

        [TestMethod]
        public void PingTest()
        {
            System.Diagnostics.Debug.WriteLine("Starting ping test: ");
            OSCMessage message = new OSCMessage("/eos/ping");
            client.Send(message);
            Thread.Sleep(5000);
        }

        [TestCleanup]
        public void Cleanup()
        {
            client.Close();
        }
    }
}
