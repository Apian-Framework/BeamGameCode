using System;
using Apian;

namespace BeamGameCode
{

    public struct PeerJoinedGameArgs {
        public string gameChannel;
        public BeamNetworkPeer peer;
        public PeerJoinedGameArgs(string g, BeamNetworkPeer p) {gameChannel=g; peer=p;}
    }
    public struct PeerLeftGameArgs {
        public string gameChannel;
        public string p2pId;
        public PeerLeftGameArgs(string g, string p) {gameChannel=g; p2pId=p;}
    }

    public interface IBeamApplication : IApianApplication
    {
        IBeamGameNet beamGameNet {get;}

        // Events
        event EventHandler<string> GameCreatedEvt; // game channel
        event EventHandler<PeerJoinedGameArgs> PeerJoinedGameEvt;
        event EventHandler<PeerLeftGameArgs> PeerLeftGameEvt;
    }

}
