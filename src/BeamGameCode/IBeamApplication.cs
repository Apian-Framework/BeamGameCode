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
    public class GameSelectedArgs {
        public enum ReturnCode {kCreate, kJoin, kCancel};
        public ReturnCode result;
        public BeamGameInfo gameInfo;
        public GameSelectedArgs( BeamGameInfo gi, ReturnCode r) { gameInfo = gi; result = r; }
    }

    public class BeamGameInfo
    {
        public ApianGroupInfo GroupInfo;

        public string GameName { get => GroupInfo.GroupName; }
        public BeamGameInfo(ApianGroupInfo agi) { GroupInfo = agi; }
    }

    public interface IBeamApplication : IApianApplication
    {
        IBeamGameNet beamGameNet {get;}

        void ExitApplication(); // relatively controlled exit via modeMgr

        // Events
        event EventHandler<PeerJoinedArgs> PeerJoinedEvt;
        event EventHandler<PeerLeftArgs> PeerLeftEvt;
        event EventHandler<BeamGameInfo> GameAnnounceEvt;

    }

}
