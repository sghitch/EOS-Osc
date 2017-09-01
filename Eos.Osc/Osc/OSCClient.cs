//
//	  UnityOSC - Open Sound Control interface for the Unity3d game engine
//
//	  Copyright (c) 2012 Jorge Garcia Martin
//
// 	  Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated 
// 	  documentation files (the "Software"), to deal in the Software without restriction, including without limitation
// 	  the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, 
// 	  and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// 
// 	  The above copyright notice and this permission notice shall be included in all copies or substantial portions 
// 	  of the Software.
//
// 	  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED 
// 	  TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL 
// 	  THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF 
// 	  CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// 	  IN THE SOFTWARE.
//

using System;
using System.Net;
using Eos.Osc.Net;
using static Eos.Osc.Net.RawAgentRelay;

namespace Eos.Osc
{
    public delegate void PacketReceivedEventHandler(OSCClient client, OSCPacket packet);

    /// <summary>
    /// Dispatches OSC messages to the specified destination address and port.
    /// </summary>

    public class OSCClient
    {
        #region Constructors
        public OSCClient(IPAddress address, int port)
        {
            _ipAddress = address;
            _port = port;
        }

        # endregion Constructors

        #region Member Variables

        private IPAddress _ipAddress;
        private int _port;
        private RawAgentRelay _client = new RawAgentRelay();

        #endregion Member Variables

        #region Accessors

        /// <summary>
        /// The IP of the OSC client
        /// </summary>
        public IPAddress ClientIP { get { return _ipAddress; } }

        /// <summary>
        /// The port of the OSC client
        /// </summary>
        public int Port { get { return _port; } }

        /// <summary>
        /// The connection status of the OSC client
        /// </summary>
        public bool Connected
        {
            get
            {
                return _client.IsConnected;
            }
        }

        #endregion Accessors

        #region Events

        public event PacketReceivedEventHandler NewOscPacketRecieved = delegate { };

        #endregion Events

        #region Public Methods

        /// <summary>
        /// Connects the client to a given remote address and port.
        /// </summary>
        public void Connect()
		{
            if (_client.IsConnected)
            {
                _client.Disconnect();
                _client.OnNewPacketReceived -= new NewPacketReceived(OnNewPacketRecieved);

            }
            try
			{
                _client.Connect(_ipAddress, _port, 1024, 1024, 5000, 5000);
                _client.OnNewPacketReceived += new NewPacketReceived(OnNewPacketRecieved);
			}
			catch (Exception e)
			{

			}
		}
        

        /// <summary>
        /// Closes the client.
        /// </summary>
        public void Close()
		{
            _client.Disconnect();
            _client.OnNewPacketReceived -= new NewPacketReceived(OnNewPacketRecieved);
        }
		
		/// <summary>
		/// Sends an OSC packet to the defined destination and address of the client.
		/// </summary>
		/// <param name="packet">
		/// A <see cref="OSCPacket"/>
		/// </param>
		public void Send(OSCPacket packet)
		{
			byte[] data = packet.BinaryData;
			try 
			{
                _client.Client.Send(data);
			}
			catch
			{
				
			}
		}
        #endregion Public Methods

        #region Private Methdos

        private void OnNewPacketRecieved(byte[] packet, RawAgentRelay agentRelay)
        {
            var oscPacket = OSCPacket.Unpack(packet);
            NewOscPacketRecieved(this, oscPacket);
            System.Diagnostics.Debug.WriteLine(oscPacket.Address);
        }

        #endregion Private Methods
    }
}

