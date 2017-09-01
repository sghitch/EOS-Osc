using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Runtime.InteropServices;
using System.Net;

namespace Eos.Osc.Net
{ 
    /// <summary>
    /// This class is provided to handle communication between client/server 
    /// Each command is sent/received in a PACKET form which makes it more efficient to control
    /// </summary>
    public class AgentRelay : NetComm
    {
        // Event handlers
        public delegate void NewPacketReceived(Packet packet, AgentRelay agentRelay);
        public event NewPacketReceived OnNewPacketReceived;

        // These commands are fixed and used in AgentRelay class for internal control process.
        private const byte HANDSHAKEREQUEST_CMDCODE = 255;
        private const byte HANDSHAKERESPONSE_CMDCODE = 254;
        private const byte INVALID_CMDCODE = 253;
        private const byte SUCCESS_CMDCODE = 252;
        private const byte WAIT_CMDCODE = 251;
        private const byte ERROR_CMDCODE = 250;

        public enum eResponseTypes
        {
            Success = SUCCESS_CMDCODE,
            InvalidCommand = INVALID_CMDCODE,
            Wait = WAIT_CMDCODE,
            Error = ERROR_CMDCODE,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi, Size = 406)]
        public class Packet
        {
            // Header
            [MarshalAs(UnmanagedType.I1)]
            public byte Type = 0;                             // =0; Reserved
            [MarshalAs(UnmanagedType.I2)]
            public ushort FragmentIndex = 0;                  // =0; Reserved
            [MarshalAs(UnmanagedType.I1)]
            public byte Command;                            // Command code
            [MarshalAs(UnmanagedType.I2)]
            public ushort DataLength;                       // Size of data to follow

            // Data
            // NOTE: IF YOU CHANGE THE SIZE, YOU HAVE TO CHANGE Constants.PacketLength too
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 400)]
            public byte[] Content;
        }

        public enum eAsyncHandshakeResult { Waiting, Failed, Success, NoResponse, NoHandshakeOccured };

        private Thread m_WorkerThread = null;
        private AutoResetEvent m_evStop;
        private AutoResetEvent m_evDataReceived;
        private Queue<Packet> m_ReceivedPackets = new Queue<Packet>(50);

        private DateTime m_AsyncHandshakeStartTimeAsync;
        private eAsyncHandshakeResult m_AsyncHandshakeResult = eAsyncHandshakeResult.Success;

        private object m_UserData = null;      // You can save anything in it and use it whenever you like!

        public object UserData
        {
            get { return m_UserData; }
            set { m_UserData = value; }
        }

        public eAsyncHandshakeResult LastHandshakeResult
        {
            get { return (m_dtLastHandshake != null) ? m_AsyncHandshakeResult : eAsyncHandshakeResult.NoHandshakeOccured; }
        }


        public override void Dispose()
        {
            base.Dispose();

            try { Disconnect(); }
            catch { }

            if (m_ReceivedPackets != null)
                m_ReceivedPackets.Clear();
            m_ReceivedPackets = null;

            OnNewPacketReceived = null;
            m_WorkerThread = null;
            if (m_evStop != null)
            {
                try { m_evStop.Close(); }
                catch { }
            }
            if (m_evDataReceived != null)
            {
                try { m_evDataReceived.Close(); }
                catch { }
            }
            m_evStop = null;
            m_evDataReceived = null;

            m_UserData = null;
            GC.SuppressFinalize(this);
        }

        public static AgentRelay FromClient(TcpClient client)
        {
            AgentRelay relay = new AgentRelay();
            relay.m_TcpClient = client;
            relay.m_RawSocket = client.Client;
            relay.StartWorker();
            return relay;
        }

        public AgentRelay()
            : base(false)
        {
            m_FaultyFlag = false;
        }

        /// <summary>
        /// If you want this class do the job and dispatch received packets, call this method
        /// </summary>
        private void StartWorker()
        {
            // Start monitoring socket
            m_WorkerThread = new Thread(new ThreadStart(WorkerThread));
            m_WorkerThread.Name = "Agent Relay";
            m_evStop = new AutoResetEvent(false);
            m_evDataReceived = new AutoResetEvent(false);
            m_WorkerThread.Start();
        }

        /// <summary>
        /// Connects to the other side (SERVER)
        /// Throw an exception if you call it twice!
        /// NOTE: Don't call this function in conjunction to FromClient()
        /// </summary>
        public void Connect(string ipAddr, int port)
        {
            Connect(IPAddress.Parse(ipAddr), port, 1024, 1024, 2000, 2000);
        }

        /// <summary>
        /// Connects to the other side (SERVER)
        /// Throw an exception if you call it twice!
        /// NOTE: Don't call this function in conjunction to FromClient()
        /// </summary>
        public new void Connect(IPAddress ipAddr, int port, int receiveBufLen, int sendBugLen, int receiveTimeout, int sendTimeout)
        {
            if (m_WorkerThread != null && m_WorkerThread.IsAlive)
                throw new Exception("AgentRelay: Already connected!");

            base.Connect(ipAddr, port, receiveBufLen, sendBugLen, receiveTimeout, sendTimeout);

            StartWorker();
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public new void Disconnect()
        {
            m_FaultyFlag = true;

            if (m_WorkerThread != null)
            {
                try
                {
                    while (m_WorkerThread.IsAlive)
                        System.Threading.Thread.Sleep(100);
                }
                catch { }
                m_WorkerThread = null;
            }

            base.Disconnect();
        }


        public void StartHandshakeAsync()
        {
            m_AsyncHandshakeResult = eAsyncHandshakeResult.Waiting;
            m_AsyncHandshakeStartTimeAsync = DateTime.Now;

            SendHandshakeRequest();

            if (m_FaultyFlag)
            {
                m_AsyncHandshakeResult = eAsyncHandshakeResult.Failed;
                return;
            }
            else
            {
                Thread th = (new Thread(new ThreadStart(AsyncHandshakeWorkerThread)));
                th.Name = "Async Handshake";
                th.Start();
            }
        }

        /// <summary>
        /// a thread to do the handshake
        /// </summary>
        private void AsyncHandshakeWorkerThread()
        {
            TimeSpan dtWait = TimeSpan.FromSeconds(10);
            while (!m_FaultyFlag)
            {
                if (m_dtLastHandshake != null)
                {
                    m_AsyncHandshakeResult = eAsyncHandshakeResult.Success;
                    return;
                }

                if (DateTime.Now - m_AsyncHandshakeStartTimeAsync > dtWait)
                {
                    m_AsyncHandshakeResult = eAsyncHandshakeResult.NoResponse;
                    return;
                }
            }
            m_AsyncHandshakeResult = eAsyncHandshakeResult.Failed;
        }

        /// <summary>
        /// SendHandshakeRequest
        /// </summary>
        private void SendHandshakeRequest()
        {
            Nullable<DateTime> prevLastHandshakeRequest = m_dtLastHandshake;
            try
            {
                m_dtLastHandshake = null;
                Packet packet = new Packet();
                packet.Command = (byte)HANDSHAKEREQUEST_CMDCODE;
                packet.DataLength = 0;
                packet.FragmentIndex = 0;
                packet.Type = 0;

                // Send request...
                SocketError socketError;
                Client.Send(StructureToByteArray(packet), 0, Marshal.SizeOf(packet), SocketFlags.None, out socketError);
                if (socketError != SocketError.Success)
                    m_FaultyFlag = true;
            }
            catch
            {
                m_FaultyFlag = true;
            }

            // Restore previouse state
            if (m_FaultyFlag)
                m_dtLastHandshake = prevLastHandshakeRequest;

        }

        /// <summary>
        /// Main Thread when we are in server mode. 
        /// Here we always monitor to see if there is any thing ready to receive and if yes, we will pick it up.
        /// NOTE: If the connection become faulty, it will return and stop working
        /// </summary>
        private void WorkerThread()
        {
            while (!m_FaultyFlag && !m_evStop.WaitOne(100, false))
            {
                try
                {
                    if (Client.Available > 0)
                    {
                        // Data available!
                        // We have to process received command
                        Packet receivedData = ReadOnePacket(3000);   // 3 seconds as timeout
                        if (receivedData == null)
                            continue;

                        if (receivedData.Command == HANDSHAKERESPONSE_CMDCODE)
                            m_dtLastHandshake = DateTime.Now;
                        else if (receivedData.Command == HANDSHAKEREQUEST_CMDCODE)
                            SendHandshakeResponse();
                        else
                        {
                            if (OnNewPacketReceived != null)
                                OnNewPacketReceived.Invoke(receivedData, this);
                            else
                            {
                                // We have to queue all received packets
                                m_ReceivedPackets.Enqueue(receivedData);
                            }
                        }
                    }
                }
                catch
                {
                    m_FaultyFlag = true;
                }
            }

            return;
        }

        /// <summary>
        /// Reads socket buffer and tries to interpret it as a packet. consider it does not work in raw mode!
        /// Returns null incase of timeout
        /// </summary>
        /// <returns></returns>
        protected Packet ReadOnePacket(int timeoutInMS)
        {
            if (m_bIsRawAgent)
                throw new Exception("NetComm is in raw mode, this function is not supported in this mode!");

            DateTime dtStart = DateTime.Now;
            TimeSpan ts = TimeSpan.FromMilliseconds(timeoutInMS);

            while ((DateTime.Now - dtStart) < ts)
            {
                if (m_FaultyFlag)
                    break;

                try
                {
                    Packet packet = new Packet();
                    int len = m_TcpClient.Available;
                    if (len < Marshal.SizeOf(packet))
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // Read data
                    SocketError socketError;
                    byte[] buff = new byte[Marshal.SizeOf(packet)];
                    int readLen = m_TcpClient.Client.Receive(buff, 0, buff.Length, SocketFlags.None, out socketError);

                    if (IS_SOCKET_FAULTY(socketError))
                    {
                        m_FaultyFlag = true;
                        return null;
                    }
                    else if (readLen != buff.Length)
                    {
                        // NOTE: Invalid packet received, just forget it!
                        return null;
                    }
                    else
                    {
                        // Convert to packet datatype
                        try { packet = (Packet)RawDataToObject(ref buff, typeof(Packet)); }
                        catch { }

                        // TODO: Continues records are not supported yet!
                        if (packet.Type != 0)
                            throw new Exception("Continues packets are not supported yet!");

                        return packet;
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine(ex.Message);
                        Console.WriteLine(ex.Message);
                    }
                    catch { }
                    m_FaultyFlag = true;    // The only reason that the exception has occured is network layer!
                }
                return null;
            }

            // Timeout
            return null;
        }

        /// <summary>
        /// As automated handshake process we will send response to the other side if we received any handshake message
        /// </summary>
        private void SendHandshakeResponse()
        {
            if (IsFaulty)
                return;
            try
            {
                Packet packet = new Packet();
                packet.Command = HANDSHAKERESPONSE_CMDCODE;
                packet.FragmentIndex = 0;
                packet.Type = 0;
                packet.DataLength = 0;

                SocketError socketEror;
                Client.Send(StructureToByteArray(packet), 0, Marshal.SizeOf(packet), SocketFlags.None, out socketEror);
                if (socketEror != SocketError.Success)
                    m_FaultyFlag = true;

                m_dtLastHandshake = DateTime.Now;
            }
            catch { m_FaultyFlag = true; }
        }

        /// <summary>
        /// If you dont like this class automatically handle the job and dispatch received packets, 
        /// you can periodically call this method to pickup every received packet
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public bool GetNextReceivedPacket(out Packet packet, int timeoutMs)
        {
            if (m_WorkerThread != null && m_WorkerThread.IsAlive)
                throw new Exception("Worker thread is alive and doing the job!");

            DateTime dtExpire = DateTime.Now + TimeSpan.FromMilliseconds(timeoutMs);

            packet = new Packet();
            do
            {
                lock (m_ReceivedPackets)
                {
                    if (m_ReceivedPackets.Count != 0)
                    {
                        packet = m_ReceivedPackets.Dequeue();
                        return true;
                    }
                }
                if (DateTime.Now >= dtExpire)
                    break;

                Thread.Sleep(Math.Min(100, timeoutMs));
            } while (true);

            return false;
        }

        /// <summary>
        /// This method is used in conjunction with GetNextReceivedPacket to clear receive buffer completely
        /// </summary>
        public void EmptyReceivedPackets()
        {
            lock (m_ReceivedPackets)
            {
                m_ReceivedPackets.Clear();
            }
        }

        /// <summary>
        /// this function copies content into packet
        /// </summary>
        public static void MakePacketContents(string content, ref Packet packet)
        {
            byte[] buf = System.Text.Encoding.ASCII.GetBytes(content);

            packet.Content = new byte[Marshal.SizeOf(packet)];

            if (buf.Length > packet.Content.Length)
                throw new Exception("MakePacketContents: Invalid packet contents!");

            packet.DataLength = (ushort)buf.Length;
            for (int i = 0; i < packet.DataLength; i++)
                packet.Content[i] = buf[i];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public static string MakeStringFromPacketContents(Packet packet)
        {
            return System.Text.Encoding.ASCII.GetString(packet.Content, 0, packet.DataLength);
        }

        /// <summary>
        /// This function is a handy method to send a simple response to the other side
        /// You can check IsFaulty flag to see the status
        /// </summary>
        public bool SendResponse(eResponseTypes type)
        {
            if (IsFaulty)
                return false;

            Packet packet = new Packet();
            packet.Command = (byte)type;
            packet.FragmentIndex = 0;
            packet.Type = 0;
            packet.DataLength = 0;
            byte[] packetBytes = StructureToByteArray(packet);

            int tryCounter = 0;
            while (tryCounter++ < MAX_TRY_COUNT)
            {
                try
                {
                    SocketError socketEror;
                    m_RawSocket.Send(packetBytes, 0, Marshal.SizeOf(packet), SocketFlags.None, out socketEror);
                    if (socketEror == SocketError.Success)
                        return true;
                    if (IS_SOCKET_FAULTY(socketEror))
                    {
                        m_FaultyFlag = true;
                        return false;
                    }
                    else
                    {
                        // We have to try again to be sure...
                        Thread.Sleep(50);
                        continue;
                    }
                }
                catch { break; }
            }

            // timeout!
            m_FaultyFlag = true;
            return false;
        }

        /// <summary>
        /// Same as SendResponse but the difference is it will send your specific command to the other side
        /// </summary>
        public bool SendMessage(int cmd)
        {
            // cmd is type of eTcpCommands
            return SendMessage(cmd, new byte[0]);
        }

        /// <summary>
        /// Same as SendResponse but the difference is it will send your specific command to the other side
        /// </summary>
        /// <param name="content">ONLY ENGLISH (ASCII)</param>
        public bool SendMessage(int cmd, string content)
        {
            return SendMessage(cmd, System.Text.Encoding.ASCII.GetBytes(content));
        }

        /// <summary>
        /// Same as SendResponse but the difference is it will send your specific command to the other side
        /// </summary>
        /// <param name="content">ONLY ENGLISH (ASCII)</param>
        public bool SendMessage(int cmd, byte[] content)
        {
            // cmd is type of eTcpCommand
            if (IsFaulty)
                return false;

            int tryCounter = 0;
            while (tryCounter++ < MAX_TRY_COUNT)
            {
                try
                {
                    Packet packet = new Packet();
                    packet.Command = (byte)cmd;
                    packet.FragmentIndex = 0;
                    packet.Type = 0;
                    if (content != null && content.Length > 0)
                    {
                        packet.Content = new byte[Marshal.SizeOf(packet)];
                        if (content.Length > packet.Content.Length)
                            throw new Exception("AgentRelay::SendMessage->Invalid content length!");
                        for (int i = 0; i < content.Length; i++)
                            packet.Content[i] = content[i];
                        packet.DataLength = (ushort)content.Length;
                    }
                    else
                        packet.DataLength = 0;

                    SocketError socketEror;
                    m_RawSocket.Send(StructureToByteArray(packet), 0, Marshal.SizeOf(packet), SocketFlags.None, out socketEror);
                    if (socketEror == SocketError.Success)
                        return true;
                    if (IS_SOCKET_FAULTY(socketEror))
                    {
                        m_FaultyFlag = true;
                        return false;
                    }
                    else
                    {
                        // We have to try again to be sure...
                        Thread.Sleep(50);
                        continue;
                    }
                }
                catch { break; }
            }

            // timeout!
            m_FaultyFlag = true;
            return false;
        }
    }
}
