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
        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected bool bGameJoined;
        protected bool bGameSetup;
        protected const float kCamTargetInterval = 10.0f;
        protected float _camTargetSecsLeft; // assign as soon as there's a bike

       protected const int kJoinGameTimeoutMs = 5000;

		public override void Start(object param = null)
        {
            logger.Info("Starting Splash");
            base.Start(param);
            appl.AddAppCore(null);
            appl.SetupCryptoAcct(true);
            DoAsyncSetupAndStartJoin();
            appl.frontend?.OnStartMode(this);
        }

        protected void DoGameSetup()
        {
            logger.Info($"{this.ModeName()}: StartSplash() Creating bikes!");
             for( int i=1;i<kSplashBikeCount; i++)
                 CreateADemoBike();
            bGameSetup = true;
        }


		public override void Loop(float frameSecs)
        {
            if (bGameJoined)
            {
                if (!bGameSetup)
                    DoGameSetup();

                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
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

            appCore.PlayerJoinedEvt -= _OnPlayerJoinedEvt;
            appCore.NewBikeEvt -= _OnNewBikeEvt;
            appl.frontend?.OnEndMode(this);
            appCore.End();

            appl.LeaveGame();
            appl.LeaveNetwork();
            appl.TearDownNetwork();

            appl.AddAppCore(null);
            return null;
        }

        protected string CreateADemoBike()
        {
            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, appCore.LocalPlayerAddr, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
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
            bool isLocal = newBike.playerAddr == appl.LocalPeer.PeerAddr;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {SID(newBike.bikeId)} Sending GO! command");
            if (isLocal)
            {
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

        //
        // Multi-threaded
        //

        protected async void DoAsyncSetupAndStartJoin()
        {
            // Setup/connect fake network
            appl.SetupNetwork("p2ploopback");
            await appl.JoinBeamNetAsync(NetworkName);

            logger.Info("Splash network joined");
            BeamGameInfo gameInfo = appl.beamGameNet.CreateBeamGameInfo(ApianGroupName, SinglePeerGroupManager.kGroupType, null, ApianGroupInfo.AnchorPostsNone, new GroupMemberLimits());
            CreateCorePair(gameInfo);
            appCore.PlayerJoinedEvt += _OnPlayerJoinedEvt;
            appCore.NewBikeEvt += _OnNewBikeEvt;

            await appl.CreateAndJoinGameAsync(gameInfo, appCore, kJoinGameTimeoutMs, false);
        }

    }



}