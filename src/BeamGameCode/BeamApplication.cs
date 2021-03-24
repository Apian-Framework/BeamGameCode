using System;
using System.Collections.Generic;
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
        public event EventHandler<GameSelectedArgs> GameSelectedEvent;
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
            _UpdateLocalPeer(); // reads stuff from settings
            beamGameNet.Connect(netConnectionStr);
        }

        // public void CreateBeamNet(BeamGameNet.BeamNetCreationData createData)
        // {
        //     _UpdateLocalPeer();  // FIXME: I'm *pretty* sure this _UpdateLocalPeer stuff was required
        //                         // by the old localData() callcack mechanism and isn;t needed anymore
        //     beamGameNet.CreateBeamNet(createData);
        // }

        public void JoinBeamNet(string networkName)
        {
            _UpdateLocalPeer();

            beamGameNet.JoinBeamNet(networkName, LocalPeer);
        }

        public void ListenForGames()
        {
            _UpdateLocalPeer();
            beamGameNet.RequestGroups();
        }

        public void  SelectGame(IDictionary<string, BeamGameInfo> existingGames)
        {
            frontend.SelectGame(existingGames); // Starts UI, or just immediately calls OnGameSelected()
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

        public void OnPeerJoinedNetwork(string p2pId, string networkId, string helloData)
        {
            BeamNetworkPeer peer = JsonConvert.DeserializeObject<BeamNetworkPeer>(helloData);
            Logger.Info($"OnPeerJoinedNetwork() {((p2pId == LocalPeer.PeerId)?"Local":"Remote")} name: {peer.Name}");
            PeerJoinedEvt.Invoke(this, new PeerJoinedArgs(networkId, peer));
        }

        public void OnPeerLeftNetwork(string p2pId, string netId)
        {
            Logger.Info($"OnPeerLeftGame({SID(p2pId)})");
            PeerLeftEvt?.Invoke(this, new PeerLeftArgs(netId, p2pId)); // Event instance might be gone
        }

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

        // IApianGameManage

        public void OnGroupAnnounce(ApianGroupInfo groupInfo)
        {
            Logger.Info($"OnGroupAnnounce({groupInfo.GroupName})");
            BeamGameInfo bgi = new BeamGameInfo(groupInfo);
            GameAnnounceEvt?.Invoke(this, bgi);
        }

        public void OnGameSelected(BeamGameInfo gameInfo, GameSelectedArgs.ReturnCode result)
        {
            Logger.Info($"OnGameSelected({gameInfo.GameName})");
            GameSelectedEvent?.Invoke(this, new GameSelectedArgs(gameInfo, result));
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