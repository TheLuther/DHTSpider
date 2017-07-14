﻿using Spider.Core.UdpServer;
using Spider.Log;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tancoder.Torrent;
using Tancoder.Torrent.BEncoding;
using Tancoder.Torrent.Client;
using Tancoder.Torrent.Dht;
using Tancoder.Torrent.Dht.Listeners;
using Tancoder.Torrent.Dht.Messages;

namespace Spider.Core
{
    public class DHTSpider : IDhtEngine
    {
        public static List<IPEndPoint> BOOTSTRAP_NODES = new List<IPEndPoint>() {
            new IPEndPoint(Dns.GetHostEntry("router.bittorrent.com").AddressList[0], 6881),
            new IPEndPoint(Dns.GetHostEntry("dht.transmissionbt.com").AddressList[0], 6881)
        };

        public DHTSpider(IPEndPoint localAddress)
        {

            LocalId = NodeId.Create();
            //listener = new DhtListener(localAddress);
            udp = new UDPService(localAddress);
            KTable = new HashSet<Node>();
            TokenManager = new EasyTokenManager();
        }
        private object locker = new object();
        public IMetaDataFilter Filter { get; set; }

        public NodeId LocalId { get; set; }

        public ITokenManager TokenManager { get; private set; }

        public HashSet<Node> KTable { get; set; }

        private bool disposed = false;
        public bool Disposed
        {
            get
            {
                return disposed;
            }
        }

        public event NewMetadataEvent NewMetadata;

        public void Add(BEncodedList nodes)
        {
            Add(Node.FromCompactNode(nodes));
        }

        public void Add(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                Add(node);
            }
        }

        public void Add(Node node)
        {
            Logger.Fatal($"Add1  {KTable.Count} {node.Id} {node.Token} {node.EndPoint}");
            lock (locker)
            {
                if (!KTable.Contains(node))
                {
                    lock (locker)
                    {
                        Logger.Fatal($"Add2  {KTable.Count} {node.Id} {node.Token} {node.EndPoint}");
                        KTable.Add(node);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
        }

        public void GetAnnounced(InfoHash infohash, IPEndPoint endpoint)
        {
            try
            {
                if (Filter == null || (Filter != null && Filter.Ignore(infohash)))
                {
                    NewMetadata?.Invoke(this, new NewMetadataEventArgs(infohash, endpoint));
                }
            }
            catch (Exception)
            {

                throw;
            }
        }

        public NodeId GetNeighborId(NodeId target)
        {
            byte[] nid = new byte[target.Bytes.Length];
            Array.Copy(target.Bytes, nid, nid.Length / 2);
            Array.Copy(LocalId.Bytes, nid.Length / 2,
                nid, nid.Length / 2, nid.Length / 2);
            return new NodeId(nid);
        }

        public void GetPeers(InfoHash infohash)
        {
            Logger.Warn($"GetPeers");
        }

        public FindPeersResult QueryFindNode(NodeId target)
        {
            var result = new FindPeersResult()
            {
                Found = false,
                //Nodes = KTable.Take(8).ToList(),
                Nodes = KTable.OrderByDescending(n => n.LastSeen).Take(8).ToList(),
            };
            return result;
        }

        public GetPeersResult QueryGetPeers(NodeId infohash)
        {
            var result = new GetPeersResult()
            {
                HasHash = false,
                Nodes = new List<Node>(),
            };
            return result;
        }


        public void Send(DhtMessage msg, IPEndPoint endpoint)
        {
            var buffer = msg.Encode();
            //listener.Send(buffer, endpoint);
            udp.Send(endpoint, buffer);
        }

        public void Start()
        {
            //listener.Start();
            //listener.MessageReceived += OnMessageReceived;
            udp.Start();
            udp.MessageReceived += OnMessageReceived;

            Task.Run(() =>
            {
                while (true)
                {
                    if (true)//Todo
                    {
                        JoinDHTNetwork();
                        MakeNeighbours();
                    }
                    Thread.Sleep(3000);
                }

            });

        }
        public void Stop()
        {
            //listener.Stop();
        }

        private void JoinDHTNetwork()
        {
            foreach (var item in BOOTSTRAP_NODES)
            {
                SendFindNodeRequest(item);
            }
        }
        private void MakeNeighbours()
        {
            foreach (var node in KTable)
            {
                SendFindNodeRequest(node.EndPoint, node.Id);
            }
            KTable.Clear();
        }

        private void SendFindNodeRequest(IPEndPoint address, NodeId nodeid = null)
        {
            FindNode msg = null;

            var nid = nodeid == null ? LocalId : GetNeighborId(nodeid);
            try
            {
                msg = new FindNode(nid, NodeId.Create());
                Send(msg, address);
            }
            catch (Exception ex)
            {
                var list = new List<string>();
                foreach (var item in msg.Parameters)
                {
                    list.Add($"[key]={item.Key}[val]={item.Value}");
                }
                var str = string.Join("&", list);
                //Logger.Fatal($"SendFindNodeRequest Error nodeid={nodeid == null} {nid} {msg.MessageType} {str}");
                //Logger.Fatal("SendFindNodeRequest Exception" + ex.Message + ex.StackTrace);
            }
        }

        private DhtListener listener;
        private UDPService udp;

        private void OnMessageReceived(byte[] buffer, IPEndPoint endpoint)
        {
            try
            {
                DhtMessage message;
                string error;
                if (MessageFactory.TryNoTraceDecodeMessage((BEncodedDictionary)BEncodedValue.Decode(buffer, 0, buffer.Length, false), out message, out error))
                {
                    if (message.MessageType.ToString() != "q")
                    {
                        //Logger.Info($"OnMessageReceived  {message.MessageType}");
                    }
                    if (message is QueryMessage)
                    {
                        message.Handle(this, new Node(message.Id, endpoint));
                    }
                }
                else
                {
                    //Logger.Error("OnMessageReceived  错误的消息");
                }

            }
            catch (Exception ex)
            {
                Logger.Error("OnMessageReceived " + ex.Message + ex.StackTrace);
            }

        }




    }
}