///////////////////////////////////////////////////////////////////////////////
//  File:       NetComm.cs
//  Version:    4.0
//
//  Author:     Amir Dashti
//  E-mail:     amirdashti@gmail.com
//
//  This code may be used in compiled form in any way you desire.
//
//  This file is provided "AS IS" with no expressed or implied warranty.
//  The author accepts no liability for any damage/loss of business for the use of
//  class, indirect and consequential damages, even if the Author has been advised
//  of the possibility of such damages.
///////////////////////////////////////////////////////////////////////////////

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
    public class RawAgentRelay : NetComm
    {
        public delegate void NewPacketReceived(byte[] packet, RawAgentRelay agentRelay);
        private Queue<byte> m_ReceivedBytes = new Queue<byte>(1024);
        public event NewPacketReceived OnNewPacketReceived;

        private Thread m_WorkerThread = null;
        private AutoResetEvent m_evStop;
        private AutoResetEvent m_evDataReceived;

        ~RawAgentRelay()
        {
            Dispose();
        }

        public override void Dispose()
        {
            base.Dispose();
            try { Disconnect(); }
            catch { }
            OnNewPacketReceived = null;
            GC.SuppressFinalize(this);
        }

        public static RawAgentRelay FromClient(Socket client)
        {
            RawAgentRelay relay = new RawAgentRelay();
            relay.m_RawSocket = client;
            relay.StartWorker();
            return relay;
        }

        public RawAgentRelay()
            : base(true)
        {
            m_FaultyFlag = false;
        }

        private void StartWorker()
        {
            // Start monitoring socket
            m_WorkerThread = new Thread(new ThreadStart(WorkerThread));
            m_WorkerThread.Name = "Raw Agent Relay";
            m_evStop = new AutoResetEvent(false);
            m_evDataReceived = new AutoResetEvent(false);
            m_WorkerThread.Start();
        }

        /// <summary>
        /// Connects to server
        /// Throw an exception if you call it twice!
        /// NOTE: Dont call this function in conjuction to FromClient()
        /// </summary>
        /// <param name="ipAddr"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public new void Connect(IPAddress ipAddr, int port, int receiveBufLen, int sendBugLen, int receiveTimeout, int sendTimeout)
        {
            if (m_WorkerThread != null && m_WorkerThread.IsAlive)
                throw new Exception("RawAgentRelay: Already connected!");

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
                while (m_WorkerThread.IsAlive)
                    System.Threading.Thread.Sleep(100);
                m_WorkerThread = null;
            }

            base.Disconnect();
        }

        /// <summary>
        /// Main Thread
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
                        byte[] buffer;
                        if (!ReadAllReceivedBytes(out buffer, 3000))
                            continue;

                        if (OnNewPacketReceived != null)
                            OnNewPacketReceived.Invoke(buffer, this);
                        else
                        {
                            lock (m_ReceivedBytes)
                            {
                                foreach (byte _b in buffer)
                                    m_ReceivedBytes.Enqueue(_b);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    Console.WriteLine(ex.Message);
                }
            }

            return;
        }

        /// <summary>
        /// Get next byte(s) from queue
        /// returns the exact read len
        /// </summary>
        /// <param name="packet"></param>
        /// <returns></returns>
        public int GetNextReceivedBytes(out byte[] buffer, int expectedLen, int timeoutMs)
        {
            DateTime dtExpire = DateTime.Now + TimeSpan.FromMilliseconds(timeoutMs);

            List<byte> readBuffer = new List<byte>();
            buffer = null;
            do
            {
                lock (m_ReceivedBytes)
                {
                    if (m_ReceivedBytes.Count != 0)
                    {
                        int len = (m_ReceivedBytes.Count > expectedLen) ? expectedLen : m_ReceivedBytes.Count;
                        for (int i = 0; i < len; i++)
                            readBuffer.Add(m_ReceivedBytes.Dequeue());
                    }
                }
                if (DateTime.Now >= dtExpire || readBuffer.Count == expectedLen)
                    break;

                Thread.Sleep(Math.Min(100, timeoutMs));
            } while (true);

            buffer = readBuffer.ToArray();
            return buffer.Length;
        }

        /// <summary>
        /// Clear all
        /// </summary>
        public void EmptyReceivedBytes()
        {
            lock (m_ReceivedBytes)
            {
                m_ReceivedBytes.Clear();
            }
        }
    }
}
