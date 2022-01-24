//#define SINGLE_THREADED
using System;
using Apian;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public class ModePractice : BeamGameMode
    {
        static public readonly string NetworkName = "LocalPracticeGame";
        static public readonly string ApianGroupName = "LocalPracticeGroup";
        public readonly int kMaxAiBikes = 11;
        protected const float kRespawnCheckInterval = 1.3f;
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool bGameJoined;
        protected bool bGameSetup;
        protected BaseBike playerBike;

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
            logger.Info("Starting Practice");
            base.Start();
            appl.AddAppCore(null);

#if !SINGLE_THREADED
            DoAsyncSetupAndStartJoin();
#else
            _DoStartup(null, param);
#endif
            appl.frontend?.OnStartMode(this);
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

            logger.Info("Practice network joined");
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
                logger.Info("Practice network joined");
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


