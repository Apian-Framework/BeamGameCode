using System.Security.Permissions;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;
using Apian;

namespace BeamGameCode
{
    public class ModeSplash : BeamGameMode
    {
        static public readonly string NetworkName = "LocalSplashNet";
        static public readonly string ApianGroupName = "LocalSplashGroup";
        static public readonly int kCmdTargetCamera = 1;
	    static public readonly int kSplashBikeCount = 12;
        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kCamTargetInterval = 10.0f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool bikesCreated;
        protected bool localPlayerJoined;

        protected float _camTargetSecsLeft = 0; // assign as soon as there's a bike

        private enum ModeState {
            JoiningNet = 1,
            JoiningGroup, // really ends with OnNewPlayer(local player)
            Playing
        }

        private ModeState _CurrentState;

		public override void Start(object param = null)
        {
            logger.Info("Starting Splash");
            base.Start();
            _DoStartup(null, param);
        }

		protected void _DoStartup(string prevModeName, object param = null)
        {
            _secsToNextRespawnCheck = kRespawnCheckInterval;
            appCore = null;
            bikesCreated = false;
            localPlayerJoined = false;
            _camTargetSecsLeft = 0;

            appl.PeerJoinedEvt += OnPeerJoinedNetEvt;
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");
            appl.JoinBeamNet(NetworkName);
            _CurrentState = ModeState.JoiningNet;
            // Now wait for OnPeerJoinedNet()
        }


		public override void Loop(float frameSecs)
        {
            if (localPlayerJoined && !bikesCreated)
            {
                logger.Info($"{this.ModeName()}: Loop() Creating bikes!");
                string cameraTargetBikeId = CreateADemoBike();
                for( int i=1;i<kSplashBikeCount; i++)
                    CreateADemoBike();

                // Note that the target bike is probably NOT created yet at this point.
                // This robably needs to happen differently
                appl.frontend?.OnStartMode(BeamModeFactory.kSplash);
                bikesCreated = true;
                _CurrentState = ModeState.Playing;
            }

            if (bikesCreated)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    // TODO: respawn with prev names/teams?
                    if (appCore.CoreData.Bikes.Count() < kSplashBikeCount)
                        CreateADemoBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }

                if ( appCore.CoreData.Bikes.Count() > 0)
                {
                    int idx = (int)UnityEngine.Random.Range(0, appCore.CoreData.Bikes.Count() - .0001f);
                    string bikeId = appCore.CoreData.Bikes.Values.ElementAt(idx).bikeId;
                    _camTargetSecsLeft -= frameSecs;
                    if (_camTargetSecsLeft <= 0)
                    {
                        appl.frontend?.DispatchModeCmd(appl.modeMgr.CurrentModeId(), kCmdTargetCamera, new TargetIdParams(){targetId=bikeId} );
                        _camTargetSecsLeft = kCamTargetInterval;
                    }
                }
            }
        }

		public override object End() { return _DoCleanup(); }

        protected object _DoCleanup()
        {
            appl.PeerJoinedEvt -= OnPeerJoinedNetEvt;
            appCore.PlayerJoinedEvt -= OnPlayerJoinedEvt;
            appCore.NewBikeEvt -= OnNewBikeEvt;
            appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
            appCore.End();
            appl.beamGameNet.LeaveNetwork();
            appl.AddAppCore(null);
            return null;
        }

        protected string CreateADemoBike()
        {
            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, appCore.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected void SetCameraTarget()
        {

        }

        public void OnPeerJoinedNetEvt(object sender, PeerJoinedArgs ga)
        {
            bool isLocal = ga.peer.PeerId == appl.LocalPeer.PeerId;
            if (isLocal && _CurrentState == ModeState.JoiningNet)
            {
                logger.Info("Splash network joined");
                // Create gameInstance and associated Apian
                BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType);
                // Create gameInstance and associated Apian
                _CreateCorePair(gameInfo);

                appCore.PlayerJoinedEvt += OnPlayerJoinedEvt;
                appCore.NewBikeEvt += OnNewBikeEvt;

                appl.CreateAndJoinGame(gameInfo, appCore);
                _CurrentState = ModeState.JoiningGroup;
                // waiting for OnPlayerJoined(localplayer)
            }
        }

        public void OnPlayerJoinedEvt(object sender, PlayerJoinedArgs ga)
        {
            _CurrentState = ModeState.Playing;
            localPlayerJoined = true;
            logger.Info("Player joined!!!");
        }

        public void OnNewBikeEvt(object sender, IBike newBike)
        {
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.peerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {newBike.bikeId} Sending GO! command");
            if (isLocal)
            {
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

    }
}