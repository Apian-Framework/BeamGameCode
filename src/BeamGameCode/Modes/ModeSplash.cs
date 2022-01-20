using System;
using System.Linq;
using Apian;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public class ModeSplash : BeamGameMode
    {
        public event EventHandler<StringEventArgs> FeTargetCameraEvt; // param is bike ID
        static public readonly string NetworkName = "LocalSplashNet";
        static public readonly string ApianGroupName = "LocalSplashGroup";
	    static public readonly int kSplashBikeCount = 12;
        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kCamTargetInterval = 10.0f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool bGameJoined;
        protected bool bGameSetup;

        protected float _camTargetSecsLeft; // assign as soon as there's a bike

#if SINGLE_THREADED
        protected enum ModeState {
            None = 0,
            JoiningNet,
            JoiningGroup,
            Playing
        }

        protected ModeState _CurrentState;
        protected bool bikesCreated;
        protected bool localPlayerJoined;
#endif


		public override void Start(object param = null)
        {
            logger.Info("Starting Splash");
            base.Start(param);
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

#if !SINGLE_THREADED
            DoAsyncSetupAndStartJoin();
#else
            _DoStartup(null, param);
#endif

            appl.frontend?.OnStartMode(this);
        }



        protected void DoGameSetup()
        {
            logger.Info($"{this.ModeName()}: StartSplash() Creating bikes!");
            string cameraTargetBikeId = CreateADemoBike();
             for( int i=1;i<kSplashBikeCount; i++)
                 CreateADemoBike();
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
                    if (appCore.CoreState.Bikes.Count < kSplashBikeCount)
                        CreateADemoBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }

                if ( appCore.CoreState.Bikes.Count > 0)
                {
                    int idx = (int)UnityEngine.Random.Range(0, appCore.CoreState.Bikes.Count - .0001f);
                    string bikeId = appCore.CoreState.Bikes.Values.ElementAt(idx).bikeId;
                    _camTargetSecsLeft -= frameSecs;
                    if (_camTargetSecsLeft <= 0)
                    {
                        logger.Verbose($"Loop(): Targetting new bike: {SID(bikeId)}");
                        FeTargetCameraEvt?.Invoke(this, new StringEventArgs(bikeId));
                        logger.Verbose($"Loop(): Done Targetting new bike");
                        _camTargetSecsLeft = kCamTargetInterval;
                    }
                }
            }
        }

		public override object End() { return _DoCleanup(); }

        private object _DoCleanup()
        {
#if SINGLE_THREADED
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
#endif
            appCore.PlayerJoinedEvt -= _OnPlayerJoinedEvt;
            appCore.NewBikeEvt -= _OnNewBikeEvt;
            appl.frontend?.OnEndMode(this);
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

        private void _OnPlayerJoinedEvt(object sender, PlayerJoinedEventArgs ga)
        {
            bGameJoined = true;
            logger.Info("Player joined!!!");
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

        //
        // Multi-threaded
        //

#if !SINGLE_THREADED
        protected async void DoAsyncSetupAndStartJoin()
        {
            // Setup/connect fake network
            appl.ConnectToNetwork("p2ploopback");
            await appl.JoinBeamNetAsync(NetworkName);

            logger.Info("Splash network joined");
            BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType);
            CreateCorePair(gameInfo);
            appCore.PlayerJoinedEvt += _OnPlayerJoinedEvt;
            appCore.NewBikeEvt += _OnNewBikeEvt;

            appl.CreateAndJoinGame(gameInfo, appCore);
            // waiting for OnPlayerJoined()
        }
#else
        // Single threaded (for WebGL, for instance)
		protected void _DoStartup(string prevModeName, object param = null)
        {
            _secsToNextRespawnCheck = kRespawnCheckInterval;
            appCore = null;
            bikesCreated = false;
            localPlayerJoined = false;
            _camTargetSecsLeft = 0;

            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt;
            appl.AddAppCore(null); // TODO: THis is beam only. Need better way. ClearGameInstances()? Init()?

            // Setup/connect fake network
            _CurrentState = ModeState.JoiningNet;
            appl.ConnectToNetwork("p2ploopback");
            appl.JoinBeamNet(NetworkName);
            // Now wait for OnPeerJoinedNet()
        }

        protected void _OnPeerJoinedNetEvt(object sender, PeerJoinedEventArgs ga)
        {
            logger.Debug($"_OnPeerJoinedNetEvt():  Peer: {ga.peer.PeerId}, Local Peer: {appl.LocalPeer.PeerId}, ModeState: {_CurrentState}");
            bool isLocal = ga.peer.PeerId == appl.LocalPeer.PeerId;
            if (isLocal && _CurrentState == ModeState.JoiningNet)
            {
                logger.Info("Splash network joined");
                // Create gameInstance and associated Apian
                BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType);
                // Create gameInstance and associated Apian
                CreateCorePair(gameInfo);

                appCore.PlayerJoinedEvt += _OnPlayerJoinedEvt;
                appCore.NewBikeEvt += _OnNewBikeEvt;

                appl.CreateAndJoinGame(gameInfo, appCore);
                _CurrentState = ModeState.JoiningGroup;
                // waiting for OnPlayerJoined(localplayer)
            } else {
                logger.Warn($"_OnPeerJoinedNetEvt() - bad juju");
            }
        }



#endif

    }



}