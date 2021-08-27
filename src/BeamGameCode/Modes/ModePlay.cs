using System;
using System.Collections.Generic;
using System.Linq;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public enum CreateMode
    {
        JoinOnly = 0,
        CreateIfNeeded = 1,
        MustCreate = 2
    }

    public class ModePlay : BeamGameMode
    {

        // Here's how I want this to work:

        // Mode begins with no AppCore, no Apian, not connection, nothing...
        // It has: net connection string and the name of the desired ApianNetwork to join.

        // - Connect to Gamenet
        // - Join the ApianNet. Wait for
        //     -> OnPeerJoinedNetwork()
        // - Get a list of available games
        // - Ask FE for a game to create/join. Wait for:
        //     -> OnGameSelected()
        // - Create/Join game. Wait for:
        //     -> OnPlayerJoinedEvt()
        // - Start playing  (could wait for others to join...)


        protected string networkName;
        protected CreateMode gameCreateMode = CreateMode.CreateIfNeeded;
        public BeamUserSettings settings;

        protected Dictionary<string, BeamGameInfo> announcedGames;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
          protected BaseBike playerBike;

        // mode substates

        protected const int kConnecting = 0;
        protected const int kJoiningNet = 2;
        protected const int kCheckingForGames = 3;
        protected const int kJoiningExistingGame = 5;
        protected const int kCreatingAndJoiningGame = 6;
        protected const int kWaitingForMembers = 7;
        protected const int kPlaying = 8;
        protected const int kFailed = 9;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kListenForGamesSecs = 2.0f; // TODO: belongs here?

		public override void Start(object param = null)
        {
            base.Start();
            announcedGames = new Dictionary<string, BeamGameInfo>();

            settings = appl.frontend.GetUserSettings();

            appl.PeerJoinedEvt += _OnPeerJoinedNetEvt;
            appl.AddAppCore(null);
            _SetState(kConnecting);
            appl.frontend?.OnStartMode(ModeId(), null );
        }

		public override void Loop(float frameSecs)
        {
            _loopFunc(frameSecs);
            _curStateSecs += frameSecs;
        }

		public override object End() {
            appl.PeerJoinedEvt -= _OnPeerJoinedNetEvt;
            if (appCore != null)
            {
                appCore.PlayerJoinedEvt -= _OnPlayerJoinedEvt;
                appCore.NewBikeEvt -= _OnNewBikeEvt;
                appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
                appCore.End();
                appl.beamGameNet.LeaveNetwork();
            }
            appl.AddAppCore(null);
            return null;
        }

        // Loopfuncs

        private async void _SetState(int newState, object startParam = null)
        {
            _curStateSecs = 0;
            _curState = newState;
            _loopFunc = _DoNothingLoop; // default
            switch (newState)
            {
            case kConnecting:
                logger.Verbose($"{(ModeName())}: SetState: kConnecting");
                try {
                    appl.ConnectToNetwork(settings.p2pConnectionString);
                } catch (Exception ex) {
                    _SetState(kFailed, ex.Message);
                    return;
                }
                _SetState(kJoiningNet);
                break;
            case kJoiningNet:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningNet");
                //try {
                    await appl.JoinBeamNetAsync(settings.apianNetworkName);
                //} catch (Exception ex) {
                //    _SetState(kFailed, ex.Message);
                //    return;
                //}
                _SetState(kCheckingForGames);
                break;
            case kCheckingForGames:
                announcedGames = await appl.GetExistingGames((int)(kListenForGamesSecs*1000));
                GameSelectedEventArgs selection = await appl.SelectGameAsync(announcedGames);
                OnGameSelected(selection);
                break;
            case kJoiningExistingGame:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningExistingGame");
                _JoinExistingGame(startParam as BeamGameInfo);
                break;
            case kCreatingAndJoiningGame:
                logger.Verbose($"{(ModeName())}: SetState: kCreatingAndJoiningGame");
                _CreateAndJoinGame(startParam as BeamGameInfo);
                break;
            case kWaitingForMembers:
                logger.Verbose($"{(ModeName())}: SetState: kWaitingForMembers");
                break;
            case kPlaying:
                logger.Verbose($"{(ModeName())}: SetState: kPlaying");
                SpawnPlayerBike();
                for (int i=0; i<settings.aiBikeCount; i++)
                    SpawnAiBike();
                _loopFunc = _PlayLoop;
                break;
            case kFailed:
                logger.Warn($"{(ModeName())}: SetState: kFailed  Reason: {(string)startParam}");
                appl.frontend.DisplayMessage(MessageSeverity.Error, (string)startParam);
                _loopFunc = _FailedLoop;
                break;
            default:
                logger.Error($"{(ModeName())}._SetState() - Unknown state: {newState}");
                break;
            }
        }

        private void _DoNothingLoop(float frameSecs) {}

        // protected void _GamesListenLoop(float frameSecs)
        // {
        //     if (_curStateSecs > kListenForGamesSecs)
        //     {
        //         // Stop listening for games
        //         appl.GameAnnounceEvt -= OnGameAnnounceEvt; // stop listening
        //         appl.GameSelectedEvent += OnGameSelectedEvt;
        //         appl.SelectGameAsync(announcedGames);
        //         _SetState(kSelectingGame); // ends with OnGameSelected()
        //     }
        // }

        private void _PlayLoop(float frameSecs)
        {
            if (settings.regenerateAiBikes)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    if (appCore.CoreState.LocalBikes(appCore.LocalPeerId).Where(ib => ib.ctrlType==BikeFactory.AiCtrl).Count() < settings.aiBikeCount)
                        SpawnAiBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

        private void _FailedLoop(float frameSecs)
        {
            //if (_curStateSecs > 5)
        }

        // utils

        private async void _DoTheStuff()
        {
            try {
                appl.ConnectToNetwork(settings.p2pConnectionString); // should be async? GameNet.Connect() currently is not
                await appl.JoinBeamNetAsync(settings.apianNetworkName);

            } catch (Exception ex) {
                _SetState(kFailed, ex.Message);
                return;
            }
        }


        private void _CreateAndJoinGame(BeamGameInfo info)
        {
            appl.CreateAndJoinGame(info, appCore);
        }

        private void _JoinExistingGame(BeamGameInfo gameInfo)
        {
            appl.JoinExistingGame(gameInfo, appCore);
        }



        // Event handlers

        private void _OnPeerJoinedNetEvt(object sender, PeerJoinedEventArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {SID(p.PeerId)}");
        }

        public void OnGameAnnounceEvt(object sender, BeamGameInfo gameInfo)
        {
            logger.Verbose($"{(ModeName())} - OnGameAnnounceEvt(): {gameInfo.GameName}");
            announcedGames[gameInfo.GameName] = gameInfo;
        }

        public void OnGameSelected( GameSelectedEventArgs args)
        {
            BeamGameInfo gameInfo = args.gameInfo;
            GameSelectedEventArgs.ReturnCode result = args.result;
            string gameName = gameInfo?.GameName;

            logger.Info($"{(ModeName())} - OnGameSelected(): {gameName ?? "<none>"}, result: {result}");

            bool targetGameExisted = (gameName != null) && announcedGames.ContainsKey(gameName);


            if (result == GameSelectedEventArgs.ReturnCode.kCancel)
            {
                _SetState( kFailed, $"OnGameSelected(): No Game Selected.");
            }
            else
            {
                if (gameInfo != null)
                {
                    CreateCorePair(gameInfo);
                    appCore.PlayerJoinedEvt += _OnPlayerJoinedEvt;
                    appCore.NewBikeEvt += _OnNewBikeEvt;
                }

                switch (result)
                {
                case GameSelectedEventArgs.ReturnCode.kCreate:
                    if (targetGameExisted)
                        _SetState(kFailed, $"Cannot create.  Beam Game \"{gameName}\" already exists");
                    else {
                        _SetState(kCreatingAndJoiningGame, gameInfo);
                    }
                    break;

                case GameSelectedEventArgs.ReturnCode.kJoin:
                    if (targetGameExisted)
                    {
                        _SetState(kJoiningExistingGame, gameInfo);
                    }
                    else
                        _SetState(kFailed, $"Apian Game \"{gameName}\" Not Found");
                    break;
                }
            }
        }

        private void _OnPlayerJoinedEvt(object sender, PlayerJoinedEventArgs ga)
        {
            bool isLocal = ga.player.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPlayerJoinedEvt() - {(isLocal?"Local":"Remote")} Member Joined: {ga.player.Name}, ID: {SID(ga.player.PeerId)}");
            if (ga.player.PeerId == appl.LocalPeer.PeerId)
            {
                appCore.RespawnPlayerEvt += _OnRespawnPlayerEvt;  // FIXME: why does this happen here?  &&&&
                //_SetState(kWaitingForMembers);
                _SetState(kPlaying);
            }
        }

        private void _OnNewBikeEvt(object sender, BikeEventArgs newBikeArgs)
        {
            IBike newBike = newBikeArgs?.ib;
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.peerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {SID(newBike.bikeId)}");
            if (isLocal)
            {
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

        private void _OnRespawnPlayerEvt(object sender, EventArgs args)
        {
            logger.Info("Respawning Player");
            SpawnPlayerBike();
            // Note that this will eventually result in a NewBikeEvt
        }

        // Gameplay control

        protected string SpawnAiBike()
        {
            BaseBike bb =  appl.CreateBaseBike( BikeFactory.AiCtrl, appCore.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ SID(bb.bikeId)})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnPlayerBike()
        {
            if (settings.localPlayerCtrlType != "none")
            {
                BaseBike bb =  appl.CreateBaseBike( settings.localPlayerCtrlType, appCore.LocalPeerId, appCore.LocalPlayer.Name, BikeDemoData.RandomTeam());
                appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb);
                logger.Debug($"{this.ModeName()}: SpawnPlayerBike({SID(bb.bikeId)})");
                return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
            }
            return null;
        }



    }
}


