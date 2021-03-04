using System;
using Apian;

namespace BeamGameCode
{

    public struct PeerJoinedArgs {
        public string channelId;
        public BeamNetworkPeer peer;
        public PeerJoinedArgs(string g, BeamNetworkPeer p) {channelId=g; peer=p;}
    }
    public struct PeerLeftArgs {
        public string channelId;
        public string p2pId;
        public PeerLeftArgs(string g, string p) {channelId=g; p2pId=p;}
    }

    public interface IBeamApplication : IApianApplication
    {
        IBeamGameNet beamGameNet {get;}

        // Events
        event EventHandler<string> NetworkCreatedEvt; // net channelId
        event EventHandler<PeerJoinedArgs> PeerJoinedEvt;
        event EventHandler<PeerLeftArgs> PeerLeftEvt;
    }

}
