using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using GameModeMgr;
using UniLog;
using static UniLog.UniLogger; // for SID()
using GameNet;
using P2pNet; // TODO: gamenet API should be all that's needed &&&&&&&&
using Apian;

namespace BeamGameCode
{
    public class BeamApplication : IModalGame, IApianApplication, IBeamApplication
    {
        //public event EventHandler<string> NetworkCreatedEvt; // net channelId
        public event EventHandler<PeerJoinedArgs> PeerJoinedEvt;
        public event EventHandler<PeerLeftArgs> PeerLeftEvt;
        public event EventHandler<BeamGameInfo> GameAnnounceEvt;
        public ModeManager modeMgr {get; private set;}
        public  IBeamGameNet beamGameNet {get; private set;}
        public IBeamFrontend frontend {get; private set;}
        public BeamNetworkPeer LocalPeer { get; private set; } = null;

        public UniLogger Logger;
        public BeamAppCore mainAppCore {get; private set;}

        public BeamApplication(BeamGameNet bgn, IBeamFrontend fe)
        {
            beamGameNet = bgn;
            beamGameNet.AddClient(this);
            frontend = fe;
            Logger = UniLogger.GetLogger("BeamApplication");
            modeMgr = new ModeManager(new BeamModeFactory(), this);

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
            _UpdateLocalPeer(); // reads stuff from settings
            beamGameNet.Connect(netConnectionStr);
        }

        // Turns out this was the wrong place to implment this - but here's an async call that
        //  completes on an event invocation
        // TODO: Get rid of this at some point
        // public async Task<bool> JoinBeamNetAsync(string networkName)
        // {
        //     _UpdateLocalPeer();
        //     TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
        //     void handler(object src, PeerJoinedArgs args)
        //     {
        //         if (args.peer?.PeerId == LocalPeer.PeerId)
        //         {
        //             tcs.TrySetResult(true);
        //         }
        //     };
        //     PeerJoinedEvt +=handler;
        //     beamGameNet.JoinBeamNet(networkName, LocalPeer);
        //     return await tcs.Task.ContinueWith<bool>( t =>  {PeerJoinedEvt -= handler; return t.Result;} );
        // }

        public async Task<PeerJoinedNetworkData> JoinBeamNetAsync(string networkName)
        {
            _UpdateLocalPeer();
            return await beamGameNet.JoinBeamNetAsync(networkName, LocalPeer);
        }

        public void OnPeerJoinedNetwork(PeerJoinedNetworkData peerData)
        {
            BeamNetworkPeer peer = JsonConvert.DeserializeObject<BeamNetworkPeer>(peerData.HelloData);
            Logger.Info($"OnPeerJoinedNetwork() {((peerData.PeerId == LocalPeer.PeerId)?"Local":"Remote")} name: {peer.Name}");
            PeerJoinedEvt?.Invoke(this, new PeerJoinedArgs(peerData.NetId, peer));
        }


        public void ListenForGames()
        {
            _UpdateLocalPeer();
            beamGameNet.RequestGroups();
        }

        public async Task<Dictionary<string, BeamGameInfo>> GetExistingGames(int waitMs)
        {
            // Can't the mode talk to baemGameNet directly?
            Dictionary<string, ApianGroupInfo> groupsDict = await beamGameNet.RequestGroupsAsync(waitMs);
            Dictionary<string, BeamGameInfo> gameDict = groupsDict.Values.Select((grp) => new BeamGameInfo(grp)).ToDictionary(gm => gm.GameName, gm => gm);
            return gameDict;
        }



        public async Task<GameSelectedArgs> SelectGameAsync(IDictionary<string, BeamGameInfo> existingGames)
        {
            GameSelectedArgs selection = await frontend.SelectGameAsync(existingGames);
            Logger.Info($"SelectGameAsync() Got result:  GameName: {selection.gameInfo.GameName} ResultCode: {selection.result}");
            return selection;
        }


        protected BeamPlayer MakeBeamPlayer() => new BeamPlayer(LocalPeer.PeerId, LocalPeer.Name);
        // FIXME: I think maybe it should go in BeamGameNet?


        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            beamGameNet.CreateAndJoinGame(gameInfo, appCore.apian, MakeBeamPlayer().ApianSerialized() );
        }
       public void JoinExistingGame(BeamGameInfo gameInfo, BeamAppCore appCore)
        {
            beamGameNet.JoinExistingGame(gameInfo, appCore.apian, MakeBeamPlayer().ApianSerialized() );
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

        private void _UpdateLocalPeer()
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
            mainAppCore?.Loop(frameSecs);
            return modeMgr.Loop(frameSecs);
        }

        // IGameNetClient
        // public void OnNetworkCreated(string netP2pChannel)
        // {
        //     Logger.Info($"OnGameCreated({netP2pChannel}");
        //     NetworkCreatedEvt?.Invoke(this, netP2pChannel);
        // }


        public void OnPeerLeftNetwork(string p2pId, string netId)
        {
            Logger.Info($"OnPeerLeftGame({SID(p2pId)})");
            PeerLeftEvt?.Invoke(this, new PeerLeftArgs(netId, p2pId)); // Event instance might be gone
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
            GameAnnounceEvt?.Invoke(this, bgi);
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