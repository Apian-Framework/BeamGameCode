using System;
using Apian;

namespace BeamGameCode
{

    public class BeamGameInfo : ApianGroupInfo
    {
        public string GameName { get => GroupName; }
        public int MaxPlayers { get => int.Parse(GroupParams["MaxPlayers"]); }
        public BeamGameInfo(ApianGroupInfo agi) : base(agi) {}
    }

    public class BeamGameStatus : ApianGroupStatus
    {
        public int PlayerCount { get => int.Parse(OtherStatus["PlayerCount"]); set => OtherStatus["PlayerCount"] = $"{value}"; }
        public int ValidatorCount { get => int.Parse(OtherStatus["ValidatorCount"]); set => OtherStatus["ValidatorCount"] = $"{value}";}
        public BeamGameStatus(ApianGroupStatus ags) : base(ags) {}
    }

    public class BeamGameAnnounceData
    {
        public BeamGameInfo GameInfo { get; }
        public BeamGameStatus GameStatus { get; }
        public BeamGameAnnounceData(GroupAnnounceResult gar)
        {
            GameInfo = new BeamGameInfo(gar.GroupInfo);
            GameStatus = new BeamGameStatus(gar.GroupStatus);
        }
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

    public class LocalPeerJoinedGameData {
        public bool success;
        public string groupId;
        public string failureReason;
        public LocalPeerJoinedGameData( bool result,  string gId,  string fr)
        {
            success = result;
            groupId = gId;
            failureReason = fr;
        }
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
