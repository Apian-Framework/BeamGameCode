using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json;
using Moq;
using BeamGameCode;

namespace P2pNetBaseTests
{
    [TestFixture]
    public class BeamPeerTests
    {

        [Test]
        public void BeamNetworkPeer_Ctor()
        {
            //public BeamNetworkPeer(string peerId, string name)
            const string p2pId = "p2pId";
            const string peerName =  "peerName";
            BeamNetworkPeer peer = new BeamNetworkPeer(p2pId, peerName);
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.PeerId, Is.EqualTo(p2pId));
            Assert.That(peer.Name, Is.EqualTo(peerName));
        }

        [Test]
        public void BeamNetworkPeer_Serialize()
        {
            const string p2pId = "p2pId";
            const string peerName =  "peerName";
            BeamNetworkPeer peer = new BeamNetworkPeer(p2pId, peerName);
            string ser = peer.ApianSerialized();
            Assert.That(ser, Is.EqualTo("[\"p2pId\",\"peerName\"]"));
        }

        [Test]
        public void BeamNetworkPeer_Deserialize()
        {
            const string p2pId = "p2pId";
            const string peerName =  "peerName";
            string serialized = $"[\"{p2pId}\",\"{peerName}\"]";

            BeamNetworkPeer peer = new BeamNetworkPeer(p2pId, peerName);
            BeamNetworkPeer p2 = BeamNetworkPeer.FromApianSerialized(serialized);
            Assert.That( peer.Name == p2.Name && peer.PeerId == p2.PeerId );
        }

    }

}