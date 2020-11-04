using System.ComponentModel;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;

namespace BeamGameCode
{
    public class ModePractice : BeamGameMode
    {
        static public readonly string GameName = "LocalPracticeGame";
        static public readonly string ApianGroupName = "LocalPracticeGroup";
        public readonly int kMaxAiBikes = 11;
        public BeamAppCore game = null;
        protected BaseBike playerBike = null;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool gameJoined;
        protected bool bikesCreated;

		public override void Start(object param = null)
        {
            base.Start();

            appl.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");
            appl.JoinNetworkGame(GameName);

            // Now wait for OnPeerJoinedGame()
        }

		public override void Loop(float frameSecs)
        {
            if (gameJoined && !bikesCreated)
            {
                // Create player bike
                string playerBikeId = SpawnPlayerBike();
                for( int i=0;i<kMaxAiBikes; i++)
                {
                    // TODO: create a list of names/teams and respawn them when the blow up?
                    // ...or do it when respawn gets called
                    SpawnAIBike();
                }
                bikesCreated = true;
            }

            if (bikesCreated)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    // TODO: respawn with prev names/teams?
                    if (game.CoreData.Bikes.Count < kMaxAiBikes)
                        SpawnAIBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {
            appl.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            game.PlayerJoinedEvt -= OnMemberJoinedGroupEvt;
            game.NewBikeEvt -= OnNewBikeEvt;
            appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
            game.End();
            appl.beamGameNet.LeaveGame();
            appl.AddAppCore(null);
            return null;
        }


        protected string SpawnPlayerBike()
        {
            // Create one the first time
            string scrName = appl.frontend.GetUserSettings().screenName;

            BaseBike bb =  appl.CreateBaseBike( BikeFactory.LocalPlayerCtrl, game.LocalPeerId, scrName, BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(game.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnAIBike(string name = null, Team team = null)
        {
            if (name == null)
                name = BikeDemoData.RandomName();

            if (team == null)
                team = BikeDemoData.RandomTeam();

            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, game.LocalPeerId, name, team);
            appl.beamGameNet.SendBikeCreateDataReq(game.ApianGroupId, bb);
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }


        public void OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt which the frontend
            // will catch and deal with. Maybe it'll point a camera at the new bike or whatever.
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

        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            bool isLocal = ga.peer.PeerId == appl.LocalPeer.PeerId;
            if (isLocal && game == null)
            {
                logger.Info("practice game joined");
                // Create gameInstance and associated Apian
                game = new BeamAppCore();
                game.PlayerJoinedEvt += OnMemberJoinedGroupEvt;
                game.NewBikeEvt += OnNewBikeEvt;

                BeamApian apian = new BeamApianSinglePeer(appl.beamGameNet, game);
                appl.AddAppCore(game);
                // Dont need to check for groups in splash
                apian.CreateNewGroup(ApianGroupName);
                BeamPlayer mb = new BeamPlayer(appl.LocalPeer.PeerId, appl.LocalPeer.Name);
                apian.JoinGroup(ApianGroupName, mb.ApianSerialized());

                appl.frontend?.OnStartMode(BeamModeFactory.kPractice, null);
                // waiting for OnGroupJoined()
            }
        }

        public void OnMemberJoinedGroupEvt(object sender, PlayerJoinedArgs ga)
        {
            game.RespawnPlayerEvt += OnRespawnPlayerEvt;
            gameJoined = true;
        }
    }
}


