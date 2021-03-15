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
    public struct GameSelectedArgs {
        public enum ReturnCode {kCreate, kJoin, kCancel};
        public ReturnCode result;
        public string gameName;

        public GameSelectedArgs( string gn, ReturnCode r) { gameName = gn; result = r; }
    }

    public interface IBeamApplication : IApianApplication
    {
        IBeamGameNet beamGameNet {get;}
        void OnGameSelected(string gameName, GameSelectedArgs.ReturnCode result);

        // Events
        event EventHandler<PeerJoinedArgs> PeerJoinedEvt;
        event EventHandler<PeerLeftArgs> PeerLeftEvt;
        event EventHandler<ApianGroupInfo> GameAnnounceEvt;
        event EventHandler<GameSelectedArgs> GameSelectedEvent;

    }

}
