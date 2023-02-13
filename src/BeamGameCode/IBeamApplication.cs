using System;
using Newtonsoft.Json;
using Apian;

namespace BeamGameCode
{

    [JsonObject(MemberSerialization.OptIn)]
    public class BeamGameInfo : ApianGroupInfo
    {
        public string GameName { get => GroupName; }
        public BeamGameInfo(ApianGroupInfo agi) : base(agi) {}
    }

    public class BeamGameStatus : ApianGroupStatus
    {
        public BeamGameStatus(ApianGroupStatus ags) : base(ags) {}
    }

    public class BeamGameAnnounceData
    {
        public BeamGameInfo GameInfo { get; }
        public BeamGameStatus GameStatus { get; set;}
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

    public class JoinRejectedEventArgs : EventArgs {
        public string channelId;
        public string reason;
        public JoinRejectedEventArgs(string s, string r) {channelId=s; reason=r;}
    }

    public class PeerLeftEventArgs : EventArgs {
        public string channelId;
        public string peerAddr;
        public PeerLeftEventArgs(string g, string p) {channelId=g; peerAddr=p;}
    }

    public class GameSelectedEventArgs : EventArgs {
        public enum ReturnCode {kCreate, kJoin, kCancel};
        public BeamGameInfo gameInfo;
        public ReturnCode result;
        public bool joinAsValidator; // deson't mean much on "cancel"
        public GameSelectedEventArgs( BeamGameInfo gi, ReturnCode r, bool jav) { gameInfo = gi; result = r; joinAsValidator=jav; }

    }

    public class GameAnnounceEventArgs : EventArgs {
        public BeamGameAnnounceData gameData;
        public GameAnnounceEventArgs( BeamGameAnnounceData gd) { gameData = gd; }
    }

    public class ChainIdEventArgs : EventArgs {
        public int chainId;
        public ChainIdEventArgs(int id) { chainId = id; }
    }

    public class ChainBlockNumberEventArgs : EventArgs {
        public int blockNumber;
        public ChainBlockNumberEventArgs(int id) { blockNumber = id; }
    }

    public class ChainAccountBalanceEventArgs : EventArgs {
        public string address;
        public int balance;
        public ChainAccountBalanceEventArgs(string a, int b) { address = a; balance = b;}
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
        BeamNetInfo NetInfo {get;}
        BeamNetworkPeer LocalPeer { get;}

        void ExitApplication(); // relatively controlled exit via modeMgr
        int CurrentGameModeId();
        void OnSwitchModeReq(int newModeId, object modeParam);
        void OnPushModeReq(int newModeId, object modeParam);
        void OnPopModeReq(object resultParam);

        void OnNetworkReady(); // tell the FE that the net is ready to work with

        void OnGameSelected( GameSelectedEventArgs selectionArgs);

        // Events
        event EventHandler<PeerJoinedEventArgs> PeerJoinedEvt;
        event EventHandler<JoinRejectedEventArgs> JoinRejectedEvt;
        event EventHandler<PeerLeftEventArgs> PeerLeftEvt;
        event EventHandler<GameAnnounceEventArgs> GameAnnounceEvt;
        event EventHandler<ChainAccountBalanceEventArgs> ChainAccountBalanceEvt;
        event EventHandler<ChainIdEventArgs> ChainIdEvt;
        event EventHandler<ChainBlockNumberEventArgs> ChainBlockNumberEvt;

    }

}
