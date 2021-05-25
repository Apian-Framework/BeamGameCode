using System.Security.Permissions;
using System;
using System.Linq;
using GameModeMgr;
using UnityEngine;
using Apian;
using static UniLog.UniLogger; // for SID()

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
        protected bool bGameJoined;
        protected bool bGameSetup;

        protected float _camTargetSecsLeft = 0; // assign as soon as there's a bike


		public override void Start(object param = null)
        {
            logger.Info("Starting Splash");
            base.Start();
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?
            DoAsyncSetupAndStartJoin();
            appl.frontend?.OnStartMode(BeamModeFactory.kSplash);
        }

        protected async void DoAsyncSetupAndStartJoin()
        {
            // Setup/connect fake network
            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");
            await appl.JoinBeamNetAsync(NetworkName);

            logger.Info("Splash network joined");
            BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType);
             _CreateCorePair(gameInfo);
            appCore.PlayerJoinedEvt += OnPlayerJoinedEvt;
            appCore.NewBikeEvt += OnNewBikeEvt;

            appl.CreateAndJoinGame(gameInfo, appCore);
            // waiting for OnPlayerJoined()
        }

        protected void DoGameSetup()
        {
            logger.Info($"{this.ModeName()}: StartSplash() Creating bikes!");
            string cameraTargetBikeId = CreateADemoBike();
             for( int i=1;i<kSplashBikeCount; i++)
                 CreateADemoBike();
            bGameSetup = true;
        }

        // public void StartSplash()
        // {
        //     logger.Info("Splash network joined");
        //     BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType);
        //     _CreateCorePair(gameInfo);
        //     appCore.PlayerJoinedEvt += OnPlayerJoinedEvt;
        //     appCore.NewBikeEvt += OnNewBikeEvt;

        //     appl.CreateAndJoinGame(gameInfo, appCore);

        //     // Note that the target bike is probably NOT created yet at this point.
        //     // This robably needs to happen differently

        // }

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
                    if (appCore.CoreState.Bikes.Count() < kSplashBikeCount)
                        CreateADemoBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }

                if ( appCore.CoreState.Bikes.Count() > 0)
                {
                    int idx = (int)UnityEngine.Random.Range(0, appCore.CoreState.Bikes.Count() - .0001f);
                    string bikeId = appCore.CoreState.Bikes.Values.ElementAt(idx).bikeId;
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
            logger.Debug($"{this.ModeName()}: SpawnAiBike({SID(bb.bikeId)})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        public void OnPlayerJoinedEvt(object sender, PlayerJoinedArgs ga)
        {
            bGameJoined = true;
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