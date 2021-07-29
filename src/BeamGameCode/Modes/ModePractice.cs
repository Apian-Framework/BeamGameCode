using System.ComponentModel;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;
using Apian;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public class ModePractice : BeamGameMode
    {
        static public readonly string networkName = "LocalPracticeGame";
        static public readonly string ApianGroupName = "LocalPracticeGroup";
        public readonly int kMaxAiBikes = 11;
         protected BaseBike playerBike;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool bGameJoined;
        protected bool bGameSetup;

		public override void Start(object param = null)
        {
            base.Start();
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?
            DoAsyncSetupAndStartJoin();
            appl.frontend?.OnStartMode(BeamModeFactory.kPractice, null);

        }

        protected async void DoAsyncSetupAndStartJoin()
        {
            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");

            await appl.JoinBeamNetAsync(networkName).ConfigureAwait(false);

            logger.Info("practice network joined");
            BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType);
            CreateCorePair(gameInfo);
            appCore.PlayerJoinedEvt += _OnMemberJoinedGroupEvt;
            appCore.NewBikeEvt += _OnNewBikeEvt;

            // Dont need to check for groups in splash
            appl.CreateAndJoinGame(gameInfo, appCore);
            // waiting for OnGroupJoined()
        }

        protected void DoGameSetup()
        {
            // Create player bike
            string playerBikeId = SpawnPlayerBike();
            for( int i=0;i<kMaxAiBikes; i++)
            {
                // TODO: create a list of names/teams and respawn them when the blow up?
                // ...or do it when respawn gets called
                SpawnAIBike();
            }
            bGameSetup = true;
        }


		public override void Loop(float frameSecs)
        {
            if (bGameJoined)
            {
                if (!bGameSetup)
                    DoGameSetup(); // synchronous

                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    // TODO: respawn with prev names/teams?
                    if (appCore.CoreState.Bikes.Count < kMaxAiBikes)
                        SpawnAIBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

		public override object End() {
            appCore.PlayerJoinedEvt -= _OnMemberJoinedGroupEvt;
            appCore.NewBikeEvt -= _OnNewBikeEvt;
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


        private void _OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt which the frontend
            // will catch and deal with. Maybe it'll point a camera at the new bike or whatever.
        }

        private void _OnNewBikeEvt(object sender, BikeEventArgs newBikeArg)
        {
            IBike newBike = newBikeArg?.ib;
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.peerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {SID(newBike.bikeId)} Sending GO! command");
            if (isLocal)
            {
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }


        private void _OnMemberJoinedGroupEvt(object sender, PlayerJoinedEventArgs ga)
        {
            appCore.RespawnPlayerEvt += _OnRespawnPlayerEvt;
            bGameJoined = true;
        }
    }
}


