using System;
using Apian;

namespace BeamGameCode
{

    public class BeamGameInfo
    {
        public ApianGroupInfo GroupInfo;
        public string GameName { get => GroupInfo.GroupName; }
        public BeamGameInfo(ApianGroupInfo agi) { GroupInfo = agi; }
    }

    public class PeerJoinedEventArgs : EventArgs {
        public string channelId;
        public BeamNetworkPeer peer;
        public PeerJoinedEventArgs(string g, BeamNetworkPeer p) {channelId=g; peer=p;}
    }
    public class PeerLeftEventArgs : EventArgs {
        public string channelId;
        public string p2pId;
        public PeerLeftEventArgs(string g, string p) {channelId=g; p2pId=p;}
    }
    public class GameSelectedEventArgs : EventArgs {
        public enum ReturnCode {kCreate, kJoin, kCancel};
        public ReturnCode result;
        public BeamGameInfo gameInfo;
        public GameSelectedEventArgs( BeamGameInfo gi, ReturnCode r) { gameInfo = gi; result = r; }
    }

    public class GameAnnounceEventArgs : EventArgs {
        public BeamGameInfo gameInfo;
        public GameAnnounceEventArgs( BeamGameInfo gi) { gameInfo = gi; }
    }


    public interface IBeamApplication : IApianApplication
    {
        IBeamGameNet beamGameNet {get;}

        void ExitApplication(); // relatively controlled exit via modeMgr

        // Events
        event EventHandler<PeerJoinedEventArgs> PeerJoinedEvt;
        event EventHandler<PeerLeftEventArgs> PeerLeftEvt;
        event EventHandler<GameAnnounceEventArgs> GameAnnounceEvt;

    }

}
