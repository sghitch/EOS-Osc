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
    public abstract class NetComm : IDisposable
    {
        protected const int MAX_TRY_COUNT = 3;
        private const int DEFAULT_CONNECT_WAIT_TIMEOUT = 1500;      // 1.5 second

        #region Properties

        protected bool m_bIsRawAgent;

        protected TcpClient m_TcpClient;    // Only used by AgentRelay
        protected Socket m_RawSocket;       // Only used by RawAgentRelay
        protected bool m_FaultyFlag;
        protected Nullable<DateTime> m_dtLastHandshake;

        /// <summary>
        /// in case the handshake has never happened, it will return DateTime.MinValue
        /// </summary>
        public DateTime LastHandshakeTime
        {
            get
            {
                if (m_dtLastHandshake.HasValue) return m_dtLastHandshake.Value;
                return DateTime.MinValue;
            }
        }

        public Socket Client
        {
            get { return m_RawSocket; }
        }

        public bool IsConnected
        {
            get
            {
                if (m_RawSocket != null && m_RawSocket.Connected)
                    return true;
                return false;
            }
        }

        /// <summary>
        /// IsFaulty
        /// </summary>
        public bool IsFaulty
        {
            get
            {
                try
                {
                    if (m_bIsRawAgent)
                        return (m_FaultyFlag || m_RawSocket == null || !m_RawSocket.Connected);
                    else
                        return (m_FaultyFlag || m_TcpClient == null || !m_TcpClient.Connected);
                }
                catch
                {
                    return true;
                }
            }
        }
        #endregion

        #region Body
        ~NetComm()
        {
            Dispose();
        }

        public virtual void Dispose()
        {
            SAFECLOSE(m_TcpClient);
            SAFECLOSE(m_RawSocket);
            GC.SuppressFinalize(this);
        }

        public NetComm(bool bIsRawAgent)
        {
            m_FaultyFlag = false;
            m_dtLastHandshake = null;
            m_TcpClient = null;
            m_RawSocket = null;
            m_bIsRawAgent = bIsRawAgent;
        }


        protected bool IS_SOCKET_FAULTY(SocketError retVal)
        {
            return (retVal != SocketError.IsConnected
                && retVal != SocketError.InProgress
                && retVal != SocketError.AlreadyInProgress
                && retVal != SocketError.IOPending
                && retVal != SocketError.NoData
                && retVal != SocketError.Success);
        }


        /// <summary>
        /// Connects to server
        /// Throw an exception if you call it twice!
        /// NOTE: Dont call this function in conjuction to FromClient()
        /// </summary>
        /// <param name="ipAddr"></param>
        /// <param name="port"></param>
        /// <returns></returns>
        public void Connect(IPAddress ipAddr, int port, int receiveBufLen, int sendBugLen, int receiveTimeout, int sendTimeout)
        {
            m_dtLastHandshake = null;
            m_FaultyFlag = false;

            try
            {
                if (m_bIsRawAgent)
                {
                    SAFECLOSE(m_RawSocket);
                    m_RawSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    IAsyncResult ar = m_RawSocket.BeginConnect(ipAddr, port, new AsyncCallback(OnConnectAsyncCallback), m_RawSocket);
                    if (!ar.AsyncWaitHandle.WaitOne(DEFAULT_CONNECT_WAIT_TIMEOUT))
                    {
                        m_RawSocket.Close();
                        throw new Exception("Connection to the other party is not possible");
                    }
                }
                else
                {
                    SAFECLOSE(m_TcpClient);
                    m_TcpClient = new TcpClient();
                    IAsyncResult ar = m_TcpClient.BeginConnect(ipAddr, port, new AsyncCallback(OnConnectAsyncCallback), m_RawSocket);
                    if (!ar.AsyncWaitHandle.WaitOne(DEFAULT_CONNECT_WAIT_TIMEOUT))
                    {
                        m_TcpClient.Close();
                        throw new Exception("Connection to the other party is not possible");
                    }
                    else
                        m_RawSocket = m_TcpClient.Client;
                }

                m_RawSocket.ReceiveBufferSize = receiveBufLen;
                m_RawSocket.SendBufferSize = sendBugLen;
                m_RawSocket.ReceiveTimeout = receiveTimeout;
                m_RawSocket.SendTimeout = sendTimeout;
            }
            catch (Exception ex)
            {
                SAFECLOSE(m_RawSocket);
                SAFECLOSE(m_TcpClient);
                throw ex;
            }
        }

        private void OnConnectAsyncCallback(IAsyncResult ar)
        {
            Socket s = (Socket)ar.AsyncState;
            try { s.EndConnect(ar); }
            catch { }
        }

        private void SAFECLOSE(TcpClient socket)
        {
            try
            {
                if (socket != null)
                {
                    if (socket.Client != null)
                        socket.Client.Close();
                    socket.Close();
                }
            }
            catch { }
            socket = null;
        }

        private void SAFECLOSE(Socket socket)
        {
            try
            {
                if (socket != null)
                    socket.Close();
            }
            catch { }
            socket = null;
        }

        /// <summary>
        /// Disconnect
        /// </summary>
        public void Disconnect()
        {
            m_FaultyFlag = true;

            if (m_RawSocket != null)
            {
                if (m_RawSocket.Connected)
                {
                    try { m_RawSocket.Disconnect(false); }
                    catch { }
                    try { m_TcpClient.Close(); }
                    catch { }
                }
            }

            SAFECLOSE(m_TcpClient);
            SAFECLOSE(m_RawSocket);
        }


        /// <summary>
        /// Reads in-buffer to the end
        /// Because it is possible to start receiving at the very last seconds of the timeout, 
        /// we wait extra 500 m.sec to ensure there is no more data available...
        /// </summary>
        protected bool ReadAllReceivedBytes(out byte[] buffer, int timeoutInMS)
        {
            if (!m_bIsRawAgent)
                throw new Exception("NetComm is not in raw mode, but you requested else!");

            List<Byte[]> array = new List<Byte[]>();
            DateTime dtLastSeenData = DateTime.Now;
            DateTime dtStart = DateTime.Now;
            TimeSpan ts = TimeSpan.FromMilliseconds(timeoutInMS);
            int totalReadBytes = 0;
            buffer = null;

            while (true)
            {
                if (m_FaultyFlag)
                    goto RETURN_WITH_FAILURE;
                if (buffer == null && (DateTime.Now - dtStart) > ts)
                    break;

                // Because it is possible to start receiving at the very last seconds of the timeout, 
                // we wait extra 500 m.sec to ensure there is no more data available...
                // NOTE: We dont check for (DateTime.Now - dtStart)>ts because 99% all packet is received
                if (buffer != null && (DateTime.Now - dtLastSeenData) > TimeSpan.FromMilliseconds(500))
                    break;

                try
                {
                    // Here we wait a little more even if we received some data, maybe there is more data available....
                    int len = m_RawSocket.Available;
                    if (len == 0)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    // Read data
                    SocketError socketError;
                    byte[] tempBuff = new byte[2000];
                    int readLen = m_RawSocket.Receive(tempBuff, 0, tempBuff.Length, SocketFlags.None, out socketError);

                    if (IS_SOCKET_FAULTY(socketError))
                        goto RETURN_WITH_FAILURE;

                    // temporarily copy to buffer
                    buffer = new byte[readLen];
                    Buffer.BlockCopy(tempBuff, 0, buffer, 0, readLen);
                    array.Add(buffer);
                    totalReadBytes += readLen;
                    dtLastSeenData = DateTime.Now;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex.Message);
                    Console.WriteLine(ex.Message);
                    m_FaultyFlag = true;
                }
            }

            // timedout?
            if (array.Count == 0)
                return false;

            // Move to final buffer
            int copiedLen = 0;
            buffer = new byte[totalReadBytes];
            for (int i = 0; i < array.Count; i++)
            {
                Buffer.BlockCopy(array[i], 0, buffer, copiedLen, array[i].Length);
                copiedLen += array[i].Length;
                array[i] = null;
            }
            array.Clear();
            return true;


            RETURN_WITH_FAILURE:
            for (int i = 0; i < array.Count; i++)
                array[i] = null;
            array.Clear();
            return false;
        }
        #endregion

        #region Some Helpers

        /// <summary>
        /// RawDataToObject
        /// </summary>
        /// <param name="rawData"></param>
        /// <param name="overlayType"></param>
        /// <returns></returns>
        public object RawDataToObject(ref byte[] rawData, Type overlayType)
        {
            object result = null;

            GCHandle pinnedRawData = GCHandle.Alloc(rawData, GCHandleType.Pinned);
            try
            {
                // Get the address of the data array
                IntPtr pinnedRawDataPtr = pinnedRawData.AddrOfPinnedObject();

                // overlay the data type on top of the raw data
                result = Marshal.PtrToStructure(pinnedRawDataPtr, overlayType);
            }
            finally
            {
                // must explicitly release
                pinnedRawData.Free();
            }

            return result;
        }

        /// <summary>
        /// StructureToByteArray
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public byte[] StructureToByteArray(object obj)
        {
            int len = Marshal.SizeOf(obj);
            byte[] arr = new byte[len];
            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, arr, 0, len);
            Marshal.FreeHGlobal(ptr);

            return arr;

        }

        /// <summary>
        /// Same as SendResponse but the difference is it will send your specific data with no change, to the other side
        /// </summary>
        /// <param name="dataToSend"></param>
        public bool SendRawData(byte[] dataToSend)
        {
            if (IsFaulty)
                return false;
            if (dataToSend == null)
                return false;
            if (dataToSend.Length > 2000)
                throw new Exception("NetCOmm::SendRawData->Invalid content length!");

            int tryCounter = 0;
            while (tryCounter++ < MAX_TRY_COUNT)
            {
                try
                {
                    SocketError socketEror;
                    m_RawSocket.Send(dataToSend, 0, dataToSend.Length, SocketFlags.None, out socketEror);
                    if (socketEror == SocketError.Success)
                        return true;
                    if (IS_SOCKET_FAULTY(socketEror))
                        m_FaultyFlag = true;
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
        #endregion
    }
}
