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
            // public BeamNetworkPeer(string peerAddr, string name)
            const string peerAddr = "peerAddr";
            const string peerName =  "peerName";
            BeamNetworkPeer peer = new BeamNetworkPeer(peerAddr, peerName);
            Assert.That(peer, Is.Not.Null);
            Assert.That(peer.PeerAddr, Is.EqualTo(peerAddr));
            Assert.That(peer.Name, Is.EqualTo(peerName));
        }

        [Test]
        public void BeamNetworkPeer_Serialize()
        {
            const string peerAddr = "peerAddr";
            const string peerName =  "peerName";
            string serialized = $"[\"{peerAddr}\",\"{peerName}\"]";

            BeamNetworkPeer peer = new BeamNetworkPeer(peerAddr, peerName);
            string ser = peer.ApianSerialized();
            Assert.That(ser, Is.EqualTo(serialized));
        }

        [Test]
        public void BeamNetworkPeer_Deserialize()
        {
            const string peerAddr = "peerAddr";
            const string peerName =  "peerName";
            string serialized = $"[\"{peerAddr}\",\"{peerName}\"]";

            BeamNetworkPeer peer = new BeamNetworkPeer(peerAddr, peerName);
            BeamNetworkPeer p2 = BeamNetworkPeer.FromApianSerialized(serialized);
            Assert.That( peer.Name == p2.Name && peer.PeerAddr == p2.PeerAddr );
        }

    }

}