﻿using System.Threading;
using System.Net.Sockets;
using System.Net;
using System;
using System.Text;

namespace URL_Shortcut_Service
{
    static class AsyncServerSocket
    {
        // Tags to determine the beginning and the end of transmission
        public const string BOT = "<~BOT~>";
        public const string EOT = "<~EOT~>";

        // The wait-signal to block the main thread while each
        // client request is being assigned to an async socket
        private static ManualResetEvent waitSignal = new ManualResetEvent(false);

        public static void LaunchServer(int port, int backlog)
        {
            // The main socket to accept connections
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                // Bind the socket
                IPAddress ip = IPAddress.Any;
                IPEndPoint endPoint = new IPEndPoint(ip, port);
                socket.Bind(endPoint);

                // Start listening
                socket.Listen(backlog);

                // Enter eternity
                while (true)
                {
                    // Reset signal
                    waitSignal.Reset();

                    // Launch an async socket to accept a connection
                    socket.BeginAccept(new AsyncCallback(Accepted), socket);

                    // Wait until a connection is established,
                    // then throw another async socket
                    waitSignal.WaitOne();
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
            finally
            {
                // Shutdown the socket
                socket.Shutdown(SocketShutdown.Both);
                socket.Close();
            }
        }

        private static void Accepted(IAsyncResult asyncResult)
        {
            /*
             * I believe the socket object that is passed to this async callback function is cloned.
             * Otherwise, the wait signal must be set after the client handler socket is acquired.
             */

            // Continue to accept another connection
            waitSignal.Set();

            // Cast back the socket
            Socket socket = (Socket)asyncResult.AsyncState;

            // Get client handler socket
            Socket clientSocket = socket.EndAccept(asyncResult);
            
            // Create a communication object to serve this client
            CommunicationObject comObj = new CommunicationObject()
            {
                connection = clientSocket
            };

            try
            {
                // Start receiving client's packets
                clientSocket.BeginReceive(comObj.buffer, 0, comObj.buffer.Length,
                    SocketFlags.None, out SocketError errorCode,
                    new AsyncCallback(Receive), comObj);

                // Log error if there's any
                if (errorCode != SocketError.Success)
                {
                    // Throw an exception upon unsuccessful receive
                    throw new Exception(errorCode.ToString());
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }

        private static void Receive(IAsyncResult asyncResult)
        {
            // Cast back the communication object
            CommunicationObject comObj = (CommunicationObject)asyncResult.AsyncState;

            // Get the client handler socket
            Socket clientSocket = comObj.connection;

            // Retrieve data from client
            int bytesReceived = clientSocket.EndReceive(asyncResult);

            // Proceed if anything is retrieved
            if (bytesReceived > 0)
            {
                // Translate the received packet
                string packet = Encoding.ASCII.GetString(comObj.buffer, 0, bytesReceived);

                // Keep whatever is received so far
                comObj.message.Append(packet);

                // Check for the beginning-of-file tag
                if (comObj.message.ToString().IndexOf(BOT) > -1)
                {
                    // Clear whatever is received so far
                    comObj.message.Clear();
                }

                // Check for the end-of-file tag
                if (comObj.message.ToString().IndexOf(EOT) > -1)
                {
                    // Remove the tag
                    comObj.message.Replace(EOT, string.Empty);

                    // Respond the client
                    Send(comObj);
                } else {
                    // Receive more packets
                    clientSocket.BeginReceive(comObj.buffer, 0, comObj.buffer.Length, 
                        SocketFlags.None, out SocketError errorCode, 
                        new AsyncCallback(Receive), comObj);

                    // Log error if there's any
                    if (errorCode != SocketError.Success)
                    {
                        Log(errorCode.ToString());
                    }
                }
            }
        }

        private static void Send(CommunicationObject comObj)
        {
            // Get the count from shared memory counter as the response
            byte[] response = Encoding.ASCII.GetBytes(GetCount());

            try
            {
                // Start sending
                comObj.connection.BeginSend(response, 0, response.Length,
                    SocketFlags.None, out SocketError errorCode,
                    new AsyncCallback(Sent), comObj);

                // Log error if there's any
                if (errorCode != SocketError.Success)
                {
                    // Throw an exception if failed to send
                    throw new Exception(errorCode.ToString());
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
        }

        private static void Sent(IAsyncResult asyncResult)
        {
            // Cast back the communication object
            CommunicationObject comObj = (CommunicationObject)asyncResult.AsyncState;

            try
            {
                // Finish sending packets
                int bytesSent = comObj.connection.EndSend(asyncResult, out SocketError errorCode);

                // Log error if there's any
                if (errorCode != SocketError.Success)
                {
                    // Throw an exception if failed to finalize
                    throw new Exception(errorCode.ToString());
                }

                /*
                 * It is possible for 'EndSend()' to fail. An exception is thrown if it does.
                 * Any code after may not get executed. Hence, the socket shutdown process i-
                 * s moved to the 'finally' block.
                 */
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
            }
            finally
            {
                /*
                 * The socket shutdown process may fail as well!
                 * Therefore, it's put in a 'try-catch' block.
                 */

                try
                {
                    // Shutdown socket
                    comObj.connection.Shutdown(SocketShutdown.Both);
                    comObj.connection.Close();
                }
                catch (Exception ex)
                {
                    Log(ex.ToString());
                }
            }
        }

        private static string GetCount()
        {
            // Get the count
            long count = SharedMemoryCounter.Capture();

            // Return the count as string
            return count.ToString();
        }

        private static void Log(string message)
        {
            // Although this is not a console app, but let it just output the error anyway
            Console.WriteLine(string.Format("{0}\t{1}\n", DateTime.Now.ToString(), message));
        }
    }
}