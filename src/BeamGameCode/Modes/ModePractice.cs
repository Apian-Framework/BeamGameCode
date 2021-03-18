using System.ComponentModel;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;
using Apian;

namespace BeamGameCode
{
    public class ModePractice : BeamGameMode
    {
        static public readonly string networkName = "LocalPracticeGame";
        static public readonly string ApianGroupName = "LocalPracticeGroup";
        public readonly int kMaxAiBikes = 11;
         protected BaseBike playerBike = null;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool netJoined;
        protected bool bikesCreated;

		public override void Start(object param = null)
        {
            base.Start();

            appl.PeerJoinedEvt += OnPeerJoinedNetEvt;
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");
            appl.JoinBeamNet(networkName);

            // Now wait for OnPeerJoinedNet()
        }

		public override void Loop(float frameSecs)
        {
            if (netJoined && !bikesCreated)
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
                    if (appCore.CoreData.Bikes.Count < kMaxAiBikes)
                        SpawnAIBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {
            appl.PeerJoinedEvt -= OnPeerJoinedNetEvt;
            appCore.PlayerJoinedEvt -= OnMemberJoinedGroupEvt;
            appCore.NewBikeEvt -= OnNewBikeEvt;
            appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
            appCore.End();
            appl.beamGameNet.LeaveNetwork();
            appl.AddAppCore(null);
            return null;
        }


        protected string SpawnPlayerBike()
        {
            // Create one the first time
            string scrName = appl.frontend.GetUserSettings().screenName;

            BaseBike bb =  appl.CreateBaseBike( BikeFactory.LocalPlayerCtrl, appCore.LocalPeerId, scrName, BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnAIBike(string name = null, Team team = null)
        {
            if (name == null)
                name = BikeDemoData.RandomName();

            if (team == null)
                team = BikeDemoData.RandomTeam();

            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, appCore.LocalPeerId, name, team);
            appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb);
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }


        public  void OnRespawnPlayerEvt(object sender, EventArgs args)
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
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

        public void OnPeerJoinedNetEvt(object sender, PeerJoinedArgs ga)
        {
            bool isLocal = ga.peer.PeerId == appl.LocalPeer.PeerId;
            if (isLocal && appCore == null)
            {
                logger.Info("practice network joined");
                BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.groupType);
                // Create gameInstance and associated Apian
                _CreateCorePair(gameInfo);
                appCore.PlayerJoinedEvt += OnMemberJoinedGroupEvt;
                appCore.NewBikeEvt += OnNewBikeEvt;

                // Dont need to check for groups in splash
                appl.CreateAndJoinGame(gameInfo, appCore);
                appl.frontend?.OnStartMode(BeamModeFactory.kPractice, null);
                // waiting for OnGroupJoined()
            }
        }

        public void OnMemberJoinedGroupEvt(object sender, PlayerJoinedArgs ga)
        {
            appCore.RespawnPlayerEvt += OnRespawnPlayerEvt;
            netJoined = true;
        }
    }
}


