//#define SINGLE_THREADED
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using ModalApplication;
using UniLog;
using static UniLog.UniLogger; // for SID()
using P2pNet; // just for PeerClockSuncInfo. Kind alame.
using GameNet;
using Apian;

#if !SINGLE_THREADED
using System.Threading.Tasks;
#endif


namespace BeamGameCode
{
    public class BeamApplication : ILoopingApp, IBeamApplication
    {
        public event EventHandler<PeerJoinedEventArgs> PeerJoinedEvt;
        public event EventHandler<PeerLeftEventArgs> PeerLeftEvt;
        public event EventHandler<GameAnnounceEventArgs> GameAnnounceEvt;
        public event EventHandler<GameSelectedEventArgs> GameSelectedEvent;

        public LoopModeManager modeMgr {get; private set;}
        public IBeamFrontend frontend {get; private set;}
        public BeamNetworkPeer LocalPeer { get; private set; }
        public BeamNetInfo NetInfo { get; private set;}  // BeamApplication keeps this updated as best as possible
        public BeamGameInfo CurrentGame { get; private set; }

        public UniLogger Logger;
        public BeamAppCore mainAppCore {get; private set;}

        public BeamApplication(BeamGameNet bgn, IBeamFrontend fe)
        {
            beamGameNet = bgn;
            beamGameNet.AddClient(this);
            frontend = fe;
            Logger = UniLogger.GetLogger("BeamApplication");
            NetInfo = new BeamNetInfo();
            modeMgr = new LoopModeManager(new BeamModeFactory(), this);

            frontend.SetBeamApplication(this);
        }

        // IBeamApplication
        public  IBeamGameNet beamGameNet {get; private set;}
        public void Start(string initialModeName)
        {
            Start( modeMgr.ModeIdForName(initialModeName));
        }
        public void ExitApplication()
        {
            modeMgr.Stop();
        }

        //
        // Tasks initiated by request from game modes
        //

        // Connect / Join network
        public void SetupNetwork(string netConnectionStr)
        {
            // This is NOT *joining* a network. Just setting up the connection
            // Connect is (for now) synchronous
              beamGameNet.SetupConnection(netConnectionStr);
        }

        // Ask to join a Beam network
        public void JoinBeamNet(string networkName)
        {
            _ResetJoinNetData();
            _CreateLocalPeer();
            beamGameNet.JoinBeamNet(networkName, LocalPeer);
            // Returns via OnPeerJoinedNetwork()
        }

#if !SINGLE_THREADED
        // Or use the async/await version
        public async Task<PeerJoinedNetworkData> JoinBeamNetAsync(string networkName)
        {
            _ResetJoinNetData();
            _CreateLocalPeer();  // reads stuff from settings  and p2p instance
            return await beamGameNet.JoinBeamNetAsync(networkName, LocalPeer); // this ends up calling OnPeerJoinedNetwork, too.
        }
#endif

        public void LeaveNetwork()
        {
            // Leave a joined BeamNet
            beamGameNet.LeaveNetwork();
            _ResetJoinNetData();
        }
        public void TearDownNetwork()
        {
            // Tear down the whole P2pNet thing (opposite of Setup)
            beamGameNet.TearDownConnection();
        }


        // Get a list of active/available Beam game instances on the net
        public void ListenForGames()
        {
            beamGameNet.RequestGroups();
            // Game (Group) announcements come back in OnGroupAnnounce()
            // assumes caller is collecting them and will time out at some point and continue
        }

#if !SINGLE_THREADED
        // ...or async/await
        public async Task<Dictionary<string, BeamGameAnnounceData>> GetExistingGamesAsync(int waitMs)
        {
            // Does NOT overwrite existing NetInfo
            Dictionary<string, GroupAnnounceResult> _ = await beamGameNet.RequestGroupsAsync(waitMs);

            // also, we need the dict keyed by game name
            return NetInfo.BeamGames.Values.ToDictionary(bgd => bgd.GameInfo.GameName, bgd => bgd);
        }
#endif

        // Now that the network is "joined" and there is information regarding how many peers there are
        // and perhaps whether there are games, ask the frontend to notify the now-waiting application
        // whether and when it should proceed to game creation/selection or perhaps cancel and disconnect.

        // While it seems odd to have this break between connecting to the net and starting a game, and even
        // weirder for the frontend to be involved, it turns out that in practice to a user there's a real
        // difference between joining the net and joining a game.

        // ACTUALLY, BeamApplication has nothing to do with this. The GameMoe just calls FE.OnNetworkReady()
        // and the FE changes modes (maybe after waiting a bit...)
        public void OnNetworkReady() => frontend.OnNetworkReady();

        // Select/join game

        // Ask the frontend to either select a game from the given list,
        // ...Or provide the data to create a new one
        // ...or cancel altogether
        public void  SelectGame(IDictionary<string, BeamGameAnnounceData> existingGames)
        {
            frontend.SelectGame(existingGames);
            // frontend displays a selection UI which eventually calls back OnGameSelected()
            // ...or the FE might just immediately call OnGameSelected() itself
        }

       public void OnGameSelected(BeamGameInfo gameInfo, GameSelectedEventArgs.ReturnCode result)
        {
            Logger.Info($"OnGameSelected({gameInfo?.GameName})");
            GameSelectedEvent?.Invoke(this, new GameSelectedEventArgs(gameInfo, result));
        }



#if !SINGLE_THREADED
        public async Task<GameSelectedEventArgs> SelectGameAsync(IDictionary<string, BeamGameAnnounceData> existingGames)
        {
            GameSelectedEventArgs selection = await frontend.SelectGameAsync(existingGames);
            Logger.Info($"SelectGameAsync() Got result:  GameName: {selection.gameInfo?.GameName} ResultCode: {selection.result}");
            return selection;
        }
#endif

        // Given the specifier for a game to join or create. Join it, or create it and then join it
        // This results on a callback to OnPeerJoinedGroup() with the local peer and the requested game (group)
        //   as parameters, which is the info that the async version return.

        // But what the game code really waits for isntead is a PlayerJoinedEvent sent from the AppCore which will come
        // soon thereafter

        public void JoinExistingGame(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            beamGameNet.JoinExistingGame(gameInfo, appCore.apian, MakeBeamPlayer().ApianSerialized() );
        }

        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            beamGameNet.CreateAndJoinGame(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized() );
        }

#if !SINGLE_THREADED
        public async Task<LocalPeerJoinedGameData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamAppCore appCore, int timeoutMs)
        {
            PeerJoinedGroupData joinData = await beamGameNet.CreateAndJoinGameAsync(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized(), timeoutMs);
            return new LocalPeerJoinedGameData(joinData.Success, joinData.GroupInfo.GroupId, joinData.Message);
        }

        public async Task<LocalPeerJoinedGameData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamAppCore appCore, int timeoutMs)
        {
            PeerJoinedGroupData joinData = await beamGameNet.JoinExistingGameAsync(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized(), timeoutMs);
            return new LocalPeerJoinedGameData(joinData.Success, joinData.GroupInfo.GroupId, joinData.Message);
        }
#endif

        public void LeaveGame()
        {
            if (CurrentGame != null)
            {
                Logger.Info($"LeaveGame() - leaving game {CurrentGame.GroupId}");
                beamGameNet.LeaveGame(CurrentGame.GroupId);
                CurrentGame = null;
            }

        }

        // Game mode  control

        public int CurrentGameModeId() => modeMgr.CurrentModeId();

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

        //
        // ILoopingApp
        //
        public void Start(int initialMode) // required by loopingapp
        {
          modeMgr.Start(initialMode);
        }
        public void End() {}
        public bool Loop(float frameSecs)
        {
            return modeMgr.Loop(frameSecs);
        }

        //
        // IApianApplication
        //
        public void AddAppCore(IApianAppCore gi)
        {
            // Beam only supports 1 game instance
            mainAppCore = gi as BeamAppCore;
            frontend.SetAppCore(gi as IBeamAppCore); // HACK
        }
        public void OnGroupAnnounce(GroupAnnounceResult groupAnn)
        {
            Logger.Info($"OnGroupAnnounce({groupAnn.GroupInfo.GroupName})");
            BeamGameAnnounceData gd = new BeamGameAnnounceData(groupAnn);
            NetInfo?.AddGame(gd);
            GameAnnounceEvt?.Invoke(this, new GameAnnounceEventArgs(gd));
        }
        public void OnGroupMemberStatus(string groupId, string peerId, ApianGroupMember.Status newStatus, ApianGroupMember.Status prevStatus)
        {
            Logger.Info($"OnGroupMemberStatus() Grp: {groupId}, Peer: {UniLogger.SID(peerId)}, Status: {newStatus}, Prev: {prevStatus}");
            frontend.OnGroupMemberStatus(groupId, peerId, newStatus, prevStatus);
        }
        public void OnPeerJoinedGroup(PeerJoinedGroupData data)
        {
            bool isLocalPeer = data?.PeerId == LocalPeer.PeerId;
            if (isLocalPeer) {
                CurrentGame = data.GroupInfo as BeamGameInfo;
            }
        }

        public void OnGroupLeaderChange(string groupId, string newLeaderId, ApianGroupMember leaderData)
        {
            string lname = leaderData != null ?  BeamPlayer.FromApianJson(leaderData.AppDataJson).Name : null;

            frontend.OnGroupLeaderChanged(groupId, newLeaderId, lname);

        }

        //
        // IGameNetClient
        //
        public void OnPeerJoinedNetwork(PeerJoinedNetworkData peerData)
        {
            BeamNetworkPeer peer = JsonConvert.DeserializeObject<BeamNetworkPeer>(peerData.HelloData);
            bool isLocalPeer = peerData.PeerId == LocalPeer.PeerId;

            if (isLocalPeer)
                NetInfo = new BeamNetInfo(beamGameNet.CurrentNetworkChannel());
            NetInfo.AddPeer(peer);

            Logger.Info($"OnPeerJoinedNetwork() {(isLocalPeer ? "Local" : "Remote")} name: {peer.Name}");
            PeerJoinedEvt?.Invoke(this, new PeerJoinedEventArgs(peerData.NetId, peer));
        }

        public void OnPeerLeftNetwork(string p2pId, string netId)
        {
            Logger.Info($"OnPeerLeftNetwork({SID(p2pId)})");
            NetInfo?.RemovePeer(p2pId);
            PeerLeftEvt?.Invoke(this, new PeerLeftEventArgs(netId, p2pId)); // Event instance might be gone
        }
        // Apian handles these at the game level. Not sure what would be useful here.
        public void OnPeerMissing(string p2pId, string netId) { }
        public void OnPeerReturned(string p2pId, string netId){ }
        public void OnPeerSync(string channel, string p2pId, PeerClockSyncInfo syncInfo) {} // stubbed
        // TODO: Be nice to be able to default-stub this somewhere.


        // Utility methods
        private void _CreateLocalPeer()
        {
            BeamUserSettings settings = frontend.GetUserSettings();
            LocalPeer = new BeamNetworkPeer(beamGameNet.LocalP2pId(), settings.screenName); // must have called
            if (LocalPeer.PeerId == null)
                throw new ArgumentNullException("LocalPeer.PeerId"); // ConnectToNetwork() not called/failed?

        }

        private void _ResetJoinNetData()
        {
            NetInfo = new BeamNetInfo(); // clear it all.
            LocalPeer = null;
        }

        protected BeamPlayer MakeBeamPlayer() => new BeamPlayer(LocalPeer.PeerId, LocalPeer.Name);
        // FIXME: I think maybe it should go in BeamGameNet?

        public BaseBike CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( mainAppCore.CoreState, mainAppCore.CurrentRunningGameTime, heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(mainAppCore.CoreState, bikeId, peerId, name, t, ctrlType, mainAppCore.CurrentRunningGameTime, pos, heading);
        }

    }
}