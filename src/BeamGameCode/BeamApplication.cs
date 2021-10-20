using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using ModalApplication;
using UniLog;
using static UniLog.UniLogger; // for SID()
using GameNet;
using Apian;

namespace BeamGameCode
{
    public class BeamApplication : ILoopingApp, IApianApplication, IBeamApplication
    {
        public event EventHandler<PeerJoinedEventArgs> PeerJoinedEvt;
        public event EventHandler<PeerLeftEventArgs> PeerLeftEvt;
        public event EventHandler<GameAnnounceEventArgs> GameAnnounceEvt;
        public LoopModeManager modeMgr {get; private set;}
        public  IBeamGameNet beamGameNet {get; private set;}
        public IBeamFrontend frontend {get; private set;}
        public BeamNetworkPeer LocalPeer { get; private set; }

        public UniLogger Logger;
        public BeamAppCore mainAppCore {get; private set;}

        public BeamApplication(BeamGameNet bgn, IBeamFrontend fe)
        {
            beamGameNet = bgn;
            beamGameNet.AddClient(this);
            frontend = fe;
            Logger = UniLogger.GetLogger("BeamApplication");
            modeMgr = new LoopModeManager(new BeamModeFactory(), this);

            frontend.SetBeamApplication(this);
        }

        public void AddAppCore(IApianAppCore gi)
        {
            // Beam only supports 1 game instance
            mainAppCore = gi as BeamAppCore;
            frontend.SetAppCore(gi as IBeamAppCore); /// TODO: this is just a hack.
        }

        public void ConnectToNetwork(string netConnectionStr)
        {
            // Connect is (for now) synchronous
              beamGameNet.Connect(netConnectionStr);
        }

        public async Task<PeerJoinedNetworkData> JoinBeamNetAsync(string networkName)
        {
            _CreateLocalPeer(); // reads stuff from settings  and p2p instance
            return await beamGameNet.JoinBeamNetAsync(networkName, LocalPeer);
        }

        public void OnPeerJoinedNetwork(PeerJoinedNetworkData peerData)
        {
            BeamNetworkPeer peer = JsonConvert.DeserializeObject<BeamNetworkPeer>(peerData.HelloData);
            Logger.Info($"OnPeerJoinedNetwork() {((peerData.PeerId == LocalPeer.PeerId)?"Local":"Remote")} name: {peer.Name}");
            PeerJoinedEvt?.Invoke(this, new PeerJoinedEventArgs(peerData.NetId, peer));
        }

        public async Task<Dictionary<string, BeamGameAnnounceData>> GetExistingGamesAsync(int waitMs)
        {
            // Can't the mode talk to baemGameNet directly?
            Dictionary<string, GroupAnnounceResult> groupsDict = await beamGameNet.RequestGroupsAsync(waitMs);
            Dictionary<string, BeamGameAnnounceData> gameDict = groupsDict.Values
                .Select((gar) => new BeamGameAnnounceData(gar))
                .ToDictionary(bgd => bgd.GameInfo.GameName, bgd => bgd);
            return gameDict;
        }

        public async Task<GameSelectedEventArgs> SelectGameAsync(IDictionary<string, BeamGameAnnounceData> existingGames)
        {
            GameSelectedEventArgs selection = await frontend.SelectGameAsync(existingGames);
            Logger.Info($"SelectGameAsync() Got result:  GameName: {selection.gameInfo?.GameName} ResultCode: {selection.result}");
            return selection;
        }

        protected BeamPlayer MakeBeamPlayer() => new BeamPlayer(LocalPeer.PeerId, LocalPeer.Name);
        // FIXME: I think maybe it should go in BeamGameNet?


        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            // Splash and Practice use this
            beamGameNet.CreateAndJoinGame(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized() );
        }

        public async Task<LocalPeerJoinedGameData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            PeerJoinedGroupData joinData = await beamGameNet.CreateAndJoinGameAsync(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized() );
            return new LocalPeerJoinedGameData(joinData.Success, joinData.GroupInfo.GroupId, joinData.Message);
        }

        // public void JoinExistingGame(BeamGameInfo gameInfo, BeamAppCore appCore)
        // {
        //     beamGameNet.JoinExistingGame(gameInfo, appCore.apian, MakeBeamPlayer().ApianSerialized() );
        // }

        public async Task<LocalPeerJoinedGameData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            PeerJoinedGroupData joinData = await beamGameNet.JoinExistingGameAsync(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized() );
            return new LocalPeerJoinedGameData(joinData.Success, joinData.GroupInfo.GroupId, joinData.Message);
        }

        public void LeaveGame(string gameId)
        {
            beamGameNet.LeaveGame(gameId);
        }

        // Mode manager
        public void ExitApplication()
        {
            modeMgr.Stop();
        }

        public void OnSwitchModeReq(int newModeId, object modeParam)
        {
           modeMgr.SwitchToMode(newModeId, modeParam);
        }

        public void OnPushModeReq(int newModeId, object modeParam)
        {
           modeMgr.PushMode(newModeId, modeParam);
        }
        public void OnPopModeReq(object resultParam)
        {
           modeMgr.PopMode(resultParam);
        }

        private void _CreateLocalPeer()
        {
            BeamUserSettings settings = frontend.GetUserSettings();
            LocalPeer = new BeamNetworkPeer(beamGameNet.LocalP2pId(), settings.screenName);
        }

        //
        // IGameInstance
        //
        public void Start(int initialMode)
        {
            modeMgr.Start(initialMode);
        }

        public void End() {}

        public bool Loop(float frameSecs)
        {
            return modeMgr.Loop(frameSecs);
        }


        public void OnPeerLeftNetwork(string p2pId, string netId)
        {
            Logger.Info($"OnPeerLeftGame({SID(p2pId)})");
            PeerLeftEvt?.Invoke(this, new PeerLeftEventArgs(netId, p2pId)); // Event instance might be gone
        }

            // Apian handles these at the game level. Not sure what would be useful here.
        public void OnPeerMissing(string p2pId, string netId) { }
        public void OnPeerReturned(string p2pId, string netId){ }

        public string LocalPeerData()
        {
            // FIXME: I think this goes away? Gets passed in to join<foo>()
            if (LocalPeer == null)
                Logger.Warn("LocalPeerData() - no local peer");
            return  JsonConvert.SerializeObject( LocalPeer);
        }

        public void SetGameNetInstance(IGameNet iGameNetInstance) {} // Stubbed.
        // FIXME: Does GameNet.SetGameNetInstance() even make sense anymore?

        public void OnPeerSync(string channel, string p2pId, long clockOffsetMs, long netLagMs) {} // stubbed
        // TODO: Be nice to be able to default-stub this somewhere.

        public void OnGroupAnnounce(ApianGroupInfo groupInfo)
        {
            Logger.Info($"OnGroupAnnounce({groupInfo.GroupName})");
            BeamGameInfo bgi = new BeamGameInfo(groupInfo);
            GameAnnounceEvt?.Invoke(this, new GameAnnounceEventArgs(bgi));
        }

        public void OnPeerJoinedGroup(PeerJoinedGroupData data)
        {

        }

        public void OnGroupMemberStatus(string groupId, string peerId, ApianGroupMember.Status newStatus, ApianGroupMember.Status prevStatus)
        {
            Logger.Info($"OnGroupMemberStatus() Grp: {groupId}, Peer: {UniLogger.SID(peerId)}, Status: {newStatus}, Prev: {prevStatus}");
        }

        // Utility methods
        public BaseBike CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( mainAppCore.CoreState, mainAppCore.CurrentRunningGameTime, heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(mainAppCore.CoreState, bikeId, peerId, name, t, ctrlType, mainAppCore.CurrentRunningGameTime, pos, heading);
        }

    }
}