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
        static public readonly string ApianGroupId = "LocalSplashId";
        static public readonly int kCmdTargetCamera = 1;
	    static public readonly int kSplashBikeCount = 12;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        public BeamAppCore game = null;
        protected bool bikesCreated;
        protected bool localPlayerJoined;

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
                game.frontend?.OnStartMode(BeamModeFactory.kSplash, new TargetIdParams{targetId = cameraTargetBikeId} );
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
            }
        }

		public override object End() {
            appl.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.PlayerJoinedEvt -= OnPlayerJoinedEvt;
            game.GroupJoinedEvt -= OnGroupJoinedEvt;
            game.NewBikeEvt -= OnNewBikeEvt;
            game.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
            game.End();
            appl.gameNet.LeaveGame();
            appl.AddAppCore(null);
            return null;
        }

        protected string CreateADemoBike()
        {
            BaseBike bb =  game.CreateBaseBike( BikeFactory.AiCtrl, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            game.PostBikeCreateData(bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            bool isLocal = ga.peer.PeerId == appl.LocalPeer.PeerId;
            if (isLocal && _CurrentState == ModeState.JoiningGame)
            {
                logger.Info("Splash game joined");
                // Create gameInstance and associated Apian
                game = new BeamAppCore(appl.frontend);
                game.PlayerJoinedEvt += OnPlayerJoinedEvt;
                game.NewBikeEvt += OnNewBikeEvt;
                BeamApian apian = new BeamApianSinglePeer(appl.gameNet, game); // This is the REAL one
                // BeamApian apian = new BeamApianCreatorServer(core.gameNet, game); // Just for quick tests of CreatorServer
                appl.AddAppCore(game);
                // Dont need to check for groups in splash
                apian.CreateNewGroup(ApianGroupId, ApianGroupName);
                BeamPlayer mb = new BeamPlayer(appl.LocalPeer.PeerId, appl.LocalPeer.Name);
                game.GroupJoinedEvt += OnGroupJoinedEvt;
                apian.JoinGroup(ApianGroupId, mb.ApianSerialized());
                _CurrentState = ModeState.JoiningGroup;
                // waiting for OnPlayerJoined(localplayer)
            }
        }

        public void OnGroupJoinedEvt(object sender, string groupId)
        {
            // Nothing results from this
            logger.Info($"{this.ModeName()}: Group {groupId} joined");
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
                game.PostBikeCommand(newBike, BikeCommand.kGo);
            }
        }

    }
}