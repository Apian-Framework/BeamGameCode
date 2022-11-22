using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using GameNet;
using P2pNet;
using Apian;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{

    public class BeamNetInfo
    {

        public P2pNetChannel NetworkChannel {get; private set;}
        public Dictionary<string, BeamNetworkPeer> BeamPeers {get; private set; }
        public Dictionary<string, BeamGameAnnounceData> BeamGames {get; private set; }

        public string NetName => NetworkChannel.Name;
        public int PeerCount => BeamPeers.Count;
        public int GameCount => BeamGames.Count;

        public BeamNetInfo(P2pNetChannel networkChannel = null)
        {
            Reset();
            NetworkChannel = networkChannel;
        }

        public void AddPeer(BeamNetworkPeer peer) => BeamPeers[peer.PeerAddr] = peer; // also use this to update
        public void RemovePeer(string peerAddr) => BeamPeers.Remove(peerAddr);

        public void AddGame(BeamGameAnnounceData game) => BeamGames[game.GameInfo.GroupId] = game; // As above, this can be used to update Status
        public void RemoveGame(string gameGroupId) => BeamGames.Remove(gameGroupId);

        protected void Reset()
        {
            NetworkChannel = null;
            BeamPeers = new Dictionary<string, BeamNetworkPeer>();
            BeamGames = new Dictionary<string, BeamGameAnnounceData>();
        }
    }
}