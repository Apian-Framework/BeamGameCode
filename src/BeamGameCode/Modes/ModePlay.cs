using System.Collections.Generic;
using System;
using System.Linq;
using GameModeMgr;
using Apian;
using UnityEngine;

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
        //protected CreateMode netCreateMode = CreateMode.CreateIfNeeded;
        protected string gameName;
        protected CreateMode gameCreateMode = CreateMode.CreateIfNeeded;
        public BeamUserSettings settings;

        protected Dictionary<string, ApianGroupInfo> announcedGames;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
        public BeamAppCore appCore = null;
        protected BaseBike playerBike = null;

        // mode substates

        protected const int kConnecting = 0;
        protected const int kJoiningNet = 2;
        protected const int kCheckingForGames = 3;
        protected const int kJoiningExistingGame = 4;
        protected const int kCreatingAndJoiningGame = 5;
        protected const int kWaitingForMembers = 6;
        protected const int kPlaying = 7;
        protected const int kFailed = 8;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kListenForGamesSecs = 2.0f; // TODO: belongs here?

		public override void Start(object param = null)
        {
            base.Start();
            announcedGames = new Dictionary<string, ApianGroupInfo>();

            settings = appl.frontend.GetUserSettings();

            appl.PeerJoinedEvt += OnPeerJoinedNetEvt;
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
            appl.PeerJoinedEvt -= OnPeerJoinedNetEvt;
            if (appCore != null)
            {
                appCore.PlayerJoinedEvt -= OnPlayerJoinedEvt;
                appCore.NewBikeEvt -= OnNewBikeEvt;
                appl.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
                appCore.End();
                appl.beamGameNet.LeaveNetwork();
            }
            appl.AddAppCore(null);
            return null;
        }

        // Loopfuncs

        protected void _SetState(int newState, object startParam = null)
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
                _JoinNetwork();
                break;
            case kCheckingForGames:
                logger.Verbose($"{(ModeName())}: SetState: kCheckingForGames");
                announcedGames.Clear();
                appl.GameAnnounceEvt += OnGameAnnounceEvt;
                appl.ListenForGames();
                _loopFunc = _GamesListenLoop;
                break;
            case kJoiningExistingGame:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningExistingGame");
                _JoinExistingGame(startParam as ApianGroupInfo);
                break;
            case kCreatingAndJoiningGame:
                logger.Verbose($"{(ModeName())}: SetState: kCreatingAndJoiningGame");
                _CreateAndJoinGame(startParam as string);
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

        protected void _DoNothingLoop(float frameSecs) {}

        protected void _GamesListenLoop(float frameSecs)
        {
            if (_curStateSecs > kListenForGamesSecs)
            {
                // TODO: Move group-knowing stuff to BeamApplication?
                // Stop listening for games
                appl.GameAnnounceEvt -= OnGameAnnounceEvt; // stop listening
                appl.GameSelectedEvent += OnGameSelectedEvt;
                appl.SelectGame(announcedGames.Keys.Distinct().ToList());
            }
        }

        protected void _PlayLoop(float frameSecs)
        {
            if (settings.regenerateAiBikes)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    if (appCore.CoreData.LocalBikes(appCore.LocalPeerId).Where(ib => ib.ctrlType==BikeFactory.AiCtrl).Count() < settings.aiBikeCount)
                        SpawnAiBike();
                    _secsToNextRespawnCheck = kRespawnCheckInterval;
                }
            }
        }

        protected void _FailedLoop(float frameSecs)
        {
            //if (_curStateSecs > 5)
        }

        // utils

        private void _ParseNetAndGame()
        {
            // Game spec syntax is "networkName/groupName"
            // If either name has an appended + character then the item should be created if it does not already exist.
            // If either name has an appended * character then the item must be created, and cannot already exist.
            // Otherwise, the item cannot be created - only joined.
            // If either name is missing then it is an error

            string gameSpecSetting;
            string[] parts = {};

            if (settings.tempSettings.TryGetValue("gameSpec", out gameSpecSetting))
                parts = gameSpecSetting.Split('/');
            else
                throw new Exception($"GameSpec (net/group) setting missing.");


            if (parts.Count() != 2)
                throw new Exception($"Bad GameSpec: {gameSpecSetting}");



            gameCreateMode = parts[1].EndsWith("+") ? CreateMode.CreateIfNeeded
                                : parts[1].EndsWith("*") ? CreateMode.MustCreate
                                    : CreateMode.JoinOnly;

            char[] trimChars = {'+','*'};
            networkName = parts[0].TrimEnd(trimChars);
            gameName = parts[1].TrimEnd(trimChars);

            logger.Verbose($"{(ModeName())}: _ParseNetAndGroup() networkName: {networkName}, gameName: {gameName} ({gameCreateMode})");
        }

        private void _JoinNetwork()
        {
            appl.JoinBeamNet(settings.apianNetworkName);
            // Wait for OnNetJoinedEvt()
        }

        private void _CreateAndJoinGame(string gameName)
        {
            // TODO: should these just be passing gameName as a string?
            appl.CreateAndJoinGame(gameName, appCore);
        }

        private void _JoinExistingGame(ApianGroupInfo gameInfo)
        {
            appl.JoinExistingGame(gameInfo, appCore);
        }

        // Event handlers

        public void OnPeerJoinedNetEvt(object sender, PeerJoinedArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPeerJoinedNetEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {p.PeerId}");
            if (isLocal)
            {
                if (_curState == kJoiningNet)
                {
                    // FIXME: is it OK for code at this level to know this much about Apian? (might be...)
                    // A LEAST this activity (creating the ApianCorePair) needs to be in its own function.
                    // Create gameinstance and ApianInstance
                    appCore = new BeamAppCore();
                    appCore.PlayerJoinedEvt += OnPlayerJoinedEvt;
                    appCore.NewBikeEvt += OnNewBikeEvt;
                    BeamApian apian = new BeamApianCreatorServer(appl.beamGameNet, appCore); // TODO: make the groupMgr type run-time spec'ed
                    appl.AddAppCore(appCore);
                    _SetState(kCheckingForGames, null);
                }
                else
                    logger.Error($"{(ModeName())} - OnNetJoinedEvt() - Wrong state: {_curState}");
            }
        }

        public void OnGameAnnounceEvt(object sender, ApianGroupInfo groupInfo)
        {
            logger.Verbose($"{(ModeName())} - OnGroupAnnounceEvt(): {groupInfo.GroupName}");
            announcedGames[groupInfo.GroupName] = groupInfo;
        }

        public void OnGameSelectedEvt(object sender, GameSelectedArgs args)
        {
            string gameName = args.gameName;
            GameSelectedArgs.ReturnCode result = args.result;

            appl.GameSelectedEvent -= OnGameSelectedEvt; // stop listening

            bool targetGameExisted = (gameName != null) && announcedGames.ContainsKey(gameName);
            if ((targetGameExisted) && (announcedGames[gameName].GroupType != CreatorServerGroupManager.groupType))
            {
                _SetState(kFailed, $"Game \"{gameName}\" Exists but is wrong type: {announcedGames[gameName].GroupType}");
                return;
            }

            switch (result)
            {
            case GameSelectedArgs.ReturnCode.kCreate:
                if (targetGameExisted)
                    _SetState(kFailed, $"Cannot create.  Beam Game \"{gameName}\" already exists");
                else {
                    _SetState(kCreatingAndJoiningGame, gameName);
                }
                break;

            case GameSelectedArgs.ReturnCode.kJoin:
                 if (targetGameExisted)
                 {
                    _SetState(kJoiningExistingGame, announcedGames[gameName]);
                 }
                else
                    _SetState(kFailed, $"Apian Game \"{gameName}\" Not Found");
                break;

            case GameSelectedArgs.ReturnCode.kCancel:
                _SetState(kFailed, $"No Game Selected.");
                break;
            }
        }

        public void OnPlayerJoinedEvt(object sender, PlayerJoinedArgs ga)
        {
            bool isLocal = ga.player.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPlayerJoinedEvt() - {(isLocal?"Local":"Remote")} Member Joined: {ga.player.Name}, ID: {ga.player.PeerId}");
            if (ga.player.PeerId == appl.LocalPeer.PeerId)
            {
                appCore.RespawnPlayerEvt += OnRespawnPlayerEvt;  // FIXME: why does this happen here?  &&&&
                //_SetState(kWaitingForMembers);
                _SetState(kPlaying);
            }
        }

        public void OnNewBikeEvt(object sender, IBike newBike)
        {
            // If it's local we need to tell it to Go!
            bool isLocal = newBike.peerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnNewBikeEvt() - {(isLocal?"Local":"Remote")} Bike created, ID: {newBike.bikeId}");
            if (isLocal)
            {
                appl.beamGameNet.SendBikeCommandReq(appCore.ApianGroupId, newBike, BikeCommand.kGo);
            }
        }

        public void OnRespawnPlayerEvt(object sender, EventArgs args)
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
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnPlayerBike()
        {
            if (settings.localPlayerCtrlType != "none")
            {
                BaseBike bb =  appl.CreateBaseBike( settings.localPlayerCtrlType, appCore.LocalPeerId, appCore.LocalPlayer.Name, BikeDemoData.RandomTeam());
                appl.beamGameNet.SendBikeCreateDataReq(appCore.ApianGroupId, bb);
                logger.Debug($"{this.ModeName()}: SpawnPlayerBike({ bb.bikeId})");
                return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
            }
            return null;
        }



    }
}


