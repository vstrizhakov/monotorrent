﻿//
// TCPConnection.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//



using System;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

namespace MonoTorrent.Client.Connections
{
    public class SocketConnection : IConnection
    {
        static readonly EventHandler<SocketAsyncEventArgs> Handler = HandleOperationCompleted;

        /// <summary>
        /// This stores a reusable 'SocketAsyncEventArgs' for every byte[] owned by ClientEngine.BufferManager
        /// </summary>
        static readonly Dictionary<byte[], SocketAsyncEventArgs> bufferCache = new Dictionary<byte[], SocketAsyncEventArgs> ();

        /// <summary>
        /// This stores reusable 'SocketAsyncEventArgs' for arbitrary byte[], or for when we are connecting
        /// to a peer and do not have a byte[] buffer to send/receive from.
        /// </summary>
        static readonly Queue<SocketAsyncEventArgs> otherCache = new Queue<SocketAsyncEventArgs> ();

        static readonly Task<int> FailedTask = Task.FromResult (0);

        /// <summary>
        /// Where possible we will use a SocketAsyncEventArgs object which has already had
        /// 'SetBuffer(byte[],int,int)' invoked on it for the given byte[]. Reusing these is
        /// much more efficient than constantly calling SetBuffer on a different 'SocketAsyncEventArgs'
        /// object.
        /// </summary>
        /// <param name="buffer">The buffer we wish to get the reusuable 'SocketAsyncEventArgs' for</param>
        /// <returns></returns>
        static SocketAsyncEventArgs GetSocketAsyncEventArgs (byte[] buffer)
        {
            SocketAsyncEventArgs args;
            lock (bufferCache) {
                if (buffer != null && ClientEngine.BufferManager.OwnsBuffer (buffer)) {
                    if (!bufferCache.TryGetValue (buffer, out args)) {
                        bufferCache[buffer] = args = new SocketAsyncEventArgs ();
                        args.SetBuffer (buffer, 0, buffer.Length);
                        args.Completed += Handler;
                    }
                } else  {
                    if (otherCache.Count == 0) {
                        args = new SocketAsyncEventArgs ();
                        args.Completed += Handler;
                    } else {
                        args = otherCache.Dequeue ();
                    }

                    if (buffer != null)
                        args.SetBuffer (buffer, 0, buffer.Length);
                }
                return args;
            }
        }

        #region Member Variables

        public byte[] AddressBytes => EndPoint.Address.GetAddressBytes();

        public bool CanReconnect => !IsIncoming;

        public bool Connected => Socket.Connected;

        EndPoint IConnection.EndPoint => EndPoint;

        public IPEndPoint EndPoint { get; }

        public bool IsIncoming { get; }

        Socket Socket { get; set; }

        public Uri Uri { get; protected set; }

		#endregion


		#region Constructors

		protected SocketConnection(Uri uri)
            : this (new Socket((uri.Scheme == "ipv4") ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp),
                  new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port), false)

        {
            Uri = uri;
        }

        protected SocketConnection(Socket socket, bool isIncoming)
            : this(socket, (IPEndPoint)socket.RemoteEndPoint, isIncoming)
        {

        }

        SocketConnection (Socket socket, IPEndPoint endpoint, bool isIncoming)
        {
            Socket = socket;
            EndPoint = endpoint;
            IsIncoming = isIncoming;
        }

        static void HandleOperationCompleted (object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success)
                ((TaskCompletionSource<int>)e.UserToken).SetException(new SocketException((int)e.SocketError));
            else
                ((TaskCompletionSource<int>)e.UserToken).SetResult(e.BytesTransferred);

            // If the 'SocketAsyncEventArgs' was used to connect, or if it was using a buffer
            // *not* managed by our BufferManager, then we should put it back in the 'other' cache.
            if (e.Buffer == null || !ClientEngine.BufferManager.OwnsBuffer (e.Buffer))
                lock (bufferCache)
                    otherCache.Enqueue (e);
        }

        #endregion


        #region Async Methods

        public Task ConnectAsync ()
        {
            var tcs = new TaskCompletionSource<int>();
            var args = GetSocketAsyncEventArgs (null);
            args.RemoteEndPoint = EndPoint;
            args.UserToken = tcs;

            if (!Socket.ConnectAsync(args))
                return Task.FromResult(true);
            return tcs.Task;
        }

        public Task<int> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            // If this has been disposed, then bail out
            if (Socket == null)
                return FailedTask;

            var tcs = new TaskCompletionSource<int>();
            var args = GetSocketAsyncEventArgs (buffer);
            args.SetBuffer (offset, count);
            args.UserToken = tcs;

            if (!Socket.ReceiveAsync(args))
                tcs.SetResult (args.BytesTransferred);
            return tcs.Task;
        }

        public Task<int> SendAsync(byte[] buffer, int offset, int count)
        {
            // If this has been disposed, then bail out
            if (Socket == null)
                return FailedTask;

            var tcs = new TaskCompletionSource<int>();
            var args = GetSocketAsyncEventArgs (buffer);
            args.SetBuffer (offset, count);
            args.UserToken = tcs;

            if (!Socket.SendAsync(args))
                tcs.SetResult (args.BytesTransferred);
            return tcs.Task;
        }

        public void Dispose()
        {
            Socket?.SafeDispose();
            Socket = null;
        }

        #endregion
    }
}