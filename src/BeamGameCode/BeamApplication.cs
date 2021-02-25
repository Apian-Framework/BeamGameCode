using System;
using Newtonsoft.Json;
using UnityEngine;
using GameModeMgr;
using UniLog;
using GameNet;
using P2pNet; // TODO: gamenet API should be all that's needed &&&&&&&&
using Apian;

namespace BeamGameCode
{
    public class BeamApplication : IModalGame, IApianApplication, IBeamApplication
    {
        public event EventHandler<string> GameCreatedEvt; // game channel
        public event EventHandler<PeerJoinedGameArgs> PeerJoinedGameEvt;
        public event EventHandler<PeerLeftGameArgs> PeerLeftGameEvt;
        public event EventHandler<ApianGroupInfo> GroupAnnounceEvt;
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
            Logger = UniLogger.GetLogger("BeamBackendInstance");
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
            _UpdateLocalPeer(); // reads stuff from settings
            beamGameNet.Connect(netConnectionStr);
        }

        public void CreateNetworkGame(BeamGameNet.GameCreationData createData)
        {
            _UpdateLocalPeer();
            beamGameNet.CreateGame(createData);
        }

        public void JoinNetworkGame(string gameName)
        {
            _UpdateLocalPeer();


            // TODO: clean this crap up!! &&&&&
            long pingMs = 2500;
            long dropMs = 5000;
            long timingMs = 15000;
            P2pNetChannelInfo chan = new P2pNetChannelInfo(gameName, gameName, dropMs, pingMs, timingMs);
            beamGameNet.JoinGame(chan);

        }

        public void ListenForGroups()
        {
            _UpdateLocalPeer();
            beamGameNet.RequestGroups();
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
        public void OnGameCreated(string gameP2pChannel)
        {
            Logger.Info($"OnGameCreated({gameP2pChannel}");
            GameCreatedEvt?.Invoke(this, gameP2pChannel);
        }

        public void OnPeerJoinedGame(string p2pId, string gameName, string helloData)
        {
            BeamNetworkPeer peer = JsonConvert.DeserializeObject<BeamNetworkPeer>(helloData);
            Logger.Info($"OnPeerJoinedGame() {((p2pId == LocalPeer.PeerId)?"Local":"Remote")} name: {peer.Name}");
            PeerJoinedGameEvt.Invoke(this, new PeerJoinedGameArgs(gameName, peer));
        }

        public void OnPeerLeftGame(string p2pId, string gameId)
        {
            Logger.Info($"OnPeerLeftGame({p2pId})");
            PeerLeftGameEvt?.Invoke(this, new PeerLeftGameArgs(gameId, p2pId)); // Event instance might be gone
        }

        // TODO: On-the-fly LocalPeerData() from GmeNet should go away (in GameNet)
        // and be replaced with JoinGame(gameId, localPeerDataStr);
        public string LocalPeerData()
        {
            // Game-level (not group-level) data about us
            if (LocalPeer == null)
                Logger.Warn("LocalPeerData() - no local peer");
            return  JsonConvert.SerializeObject( LocalPeer);
        }

        public void SetGameNetInstance(IGameNet iGameNetInstance) {} // Stubbed.
        // TODO: Does GameNet.SetGameNetInstance() even make sense anymore?

        public void OnPeerSync(string p2pId, long clockOffsetMs, long netLagMs) {} // stubbed
        // TODO: Maybe stub this is an ApianGameManagerBase class that this derives from?

        // IApianGameManage

        public void OnGroupAnnounce(string groupId, string groupType, string creatorId, string groupName)
        {
            Logger.Info($"OnGroupData({groupId})");
            GroupAnnounceEvt?.Invoke(this, new ApianGroupInfo(groupType, groupId, creatorId, groupName));
        }

        // Utility methods
        public BaseBike CreateBaseBike(string ctrlType, string peerId, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( mainAppCore.CoreData, mainAppCore.CurrentRunningGameTime, heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString();
            return  new BaseBike(mainAppCore.CoreData, bikeId, peerId, name, t, ctrlType, mainAppCore.CurrentRunningGameTime, pos, heading);
        }

    }
}