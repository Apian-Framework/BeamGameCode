using System.Security.Permissions;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamGameCode
{
    public class ModeSplash : BeamGameMode
    {
        static public readonly string GameName = "LocalSplashGame";
        static public readonly string ApianGroupName = "LocalSplashGroup";
        static public readonly int kCmdTargetCamera = 1;
	    static public readonly int kSplashBikeCount = 12;
        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kCamTargetInterval = 10.0f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        public BeamAppCore game = null;
        protected bool bikesCreated;
        protected bool localPlayerJoined;

        protected float _camTargetSecsLeft = 0; // assign as soon as there's a bike

        private enum ModeState {
            JoiningGame = 1,
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
            game = null;
            bikesCreated = false;
            localPlayerJoined = false;
            _camTargetSecsLeft = 0;

            appl.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");
            appl.JoinNetworkGame(GameName);
            _CurrentState = ModeState.JoiningGame;
            // Now wait for OnPeerJoinedGame()
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
                    if (game.CoreData.Bikes.Count() < kSplashBikeCount)
                        CreateADemoBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }

                if ( game.CoreData.Bikes.Count() > 0)
                {
                    int idx = (int)UnityEngine.Random.Range(0, game.CoreData.Bikes.Count() - .0001f);
                    string bikeId = game.CoreData.Bikes.Values.ElementAt(idx).bikeId;
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
            appl.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.PlayerJoinedEvt -= OnPlayerJoinedEvt;
            game.NewBikeEvt -= OnNewBikeEvt;
            appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
            game.End();
            appl.beamGameNet.LeaveGame();
            appl.AddAppCore(null);
            return null;
        }

        protected string CreateADemoBike()
        {
            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(game.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected void SetCameraTarget()
        {

        }

        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            bool isLocal = ga.peer.PeerId == appl.LocalPeer.PeerId;
            if (isLocal && _CurrentState == ModeState.JoiningGame)
            {
                logger.Info("Splash game joined");
                // Create gameInstance and associated Apian
                game = new BeamAppCore();
                game.PlayerJoinedEvt += OnPlayerJoinedEvt;
                game.NewBikeEvt += OnNewBikeEvt;
                BeamApian apian = new BeamApianSinglePeer(appl.beamGameNet, game); // This is the REAL one
                appl.AddAppCore(game);
                // Dont need to check for groups in splash
                apian.CreateNewGroup(ApianGroupName);
                BeamPlayer mb = new BeamPlayer(appl.LocalPeer.PeerId, appl.LocalPeer.Name);
                apian.JoinGroup(ApianGroupName, mb.ApianSerialized());
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
                appl.beamGameNet.SendBikeCommandReq(game.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

    }
}