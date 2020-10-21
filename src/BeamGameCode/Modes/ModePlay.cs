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
        protected string gameName;
        protected CreateMode gameCreateMode = CreateMode.CreateIfNeeded;
        protected string groupName;
        protected CreateMode groupCreateMode = CreateMode.CreateIfNeeded;
        public BeamUserSettings settings;

        protected Dictionary<string, ApianGroupInfo> announcedGroups;

        protected float _secsToNextRespawnCheck = kRespawnCheckInterval;
        protected int _curState;
        protected float _curStateSecs;
        protected delegate void LoopFunc(float f);
        protected LoopFunc _loopFunc;
        public BeamAppCore game = null;
        protected BaseBike playerBike = null;

        // mode substates

        protected const int kConnecting = 0;
        protected const int kCreatingGame = 1;
        protected const int kJoiningGame = 2;
        protected const int kCheckingForGroups = 3;
        protected const int kJoiningGroup = 4;
        protected const int kWaitingForMembers = 5;
        protected const int kPlaying = 6;
        protected const int kFailed = 7;

        protected const float kRespawnCheckInterval = 1.3f;
        protected const float kListenForGroupsSecs = 2.0f; // TODO: belongs here?

		public override void Start(object param = null)
        {
            base.Start();
            announcedGroups = new Dictionary<string, ApianGroupInfo>();

            settings = appl.frontend.GetUserSettings();

            appl.GameCreatedEvt += OnGameCreatedEvt;
            appl.PeerJoinedGameEvt += OnPeerJoinedGameEvt;
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
            appl.GameCreatedEvt -= OnGameCreatedEvt;
            appl.PeerJoinedGameEvt -= OnPeerJoinedGameEvt;
            if (game != null)
            {
                game.PlayerJoinedEvt -= OnPlayerJoinedEvt;
                game.NewBikeEvt -= OnNewBikeEvt;
                game.frontend?.OnEndMode(appl.modeMgr.CurrentModeId(), null);
                game.End();
                appl.gameNet.LeaveGame();
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
                    _ParseGameAndGroup();
                    appl.ConnectToNetwork(settings.p2pConnectionString);
                } catch (Exception ex) {
                    _SetState(kFailed, ex.Message);
                    return;
                }
                if (gameName == null)
                    _SetState(kCreatingGame, new BeamGameNet.GameCreationData());
                else
                    _SetState(kJoiningGame, gameName);
                break;
            case kCreatingGame:
                logger.Verbose($"{(ModeName())}: SetState: kCreatingGame");
                appl.CreateNetworkGame((BeamGameNet.GameCreationData)startParam);
                // Wait for OnGameCreatedEvt()
                break;
            case kJoiningGame:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningGame");
                appl.JoinNetworkGame((string)startParam);
                // Wait for OnGameJoinedEvt()
                break;
            case kCheckingForGroups:
                logger.Verbose($"{(ModeName())}: SetState: kCheckingForGroups");
                announcedGroups.Clear();
                appl.GroupAnnounceEvt += OnGroupAnnounceEvt;
                appl.ListenForGroups();
                _loopFunc = _GroupListenLoop;
                break;
            case kJoiningGroup:
                logger.Verbose($"{(ModeName())}: SetState: kJoiningGroup");
                _JoinGroup();
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

        protected void _GroupListenLoop(float frameSecs)
        {
            if (_curStateSecs > kListenForGroupsSecs)
            {
                // TODO: Hoist all this!!!
                // Stop listening for groups and either create or join (or fail)
                appl.GroupAnnounceEvt -= OnGroupAnnounceEvt; // stop listening
                bool targetGroupExisted = (groupName != null) && announcedGroups.ContainsKey(groupName);

                switch (groupCreateMode)
                {
                case CreateMode.JoinOnly:
                    if (targetGroupExisted)
                    {
                        game.apian.InitExistingGroup(announcedGroups[groupName]); // Like create, but for a remotely-created group
                        _SetState(kJoiningGroup);
                    }
                    else
                        _SetState(kFailed, $"Apian Group \"{groupName}\" Not Found");
                    break;
                case CreateMode.CreateIfNeeded:
                    if (targetGroupExisted)
                        game.apian.InitExistingGroup(announcedGroups[groupName]);
                    else
                        _CreateGroup();
                    _SetState(kJoiningGroup);
                    break;
                case CreateMode.MustCreate:
                    if (targetGroupExisted)
                        _SetState(kFailed, "Cannot create.  Apian Group already exists");
                    else {
                        _CreateGroup();
                        _SetState(kJoiningGroup);
                    }
                    break;
                }
            }
        }

        protected void _PlayLoop(float frameSecs)
        {
            if (settings.regenerateAiBikes)
            {
                _secsToNextRespawnCheck -= frameSecs;
                if (_secsToNextRespawnCheck <= 0)
                {
                    if (game.CoreData.LocalBikes(game.LocalPeerId).Where(ib => ib.ctrlType==BikeFactory.AiCtrl).Count() < settings.aiBikeCount)
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

        private void _ParseGameAndGroup()
        {
            // Game spec syntax is "gameName/groupName"
            // If either name has an appended + character then the item should be created if it does not already exist.
            // If either name has an appended * character then the item must be created, and cannot already exist.
            // Otherwise, the item cannot be created - only joined.
            // If either name is missing then it is an error

            string gameSpecSetting;
            string[] parts = {};

            if (settings.tempSettings.TryGetValue("gameSpec", out gameSpecSetting))
                parts = gameSpecSetting.Split('/');
            else
                throw new Exception($"GameSpec (name/group) setting missing.");


            if (parts.Count() != 2)
                throw new Exception($"Bad GameSpec: {gameSpecSetting}");

            gameCreateMode = parts[0].EndsWith("+") ? CreateMode.CreateIfNeeded
                                : parts[0].EndsWith("*") ? CreateMode.MustCreate
                                    : CreateMode.JoinOnly;

            groupCreateMode = parts[1].EndsWith("+") ? CreateMode.CreateIfNeeded
                                : parts[1].EndsWith("*") ? CreateMode.MustCreate
                                    : CreateMode.JoinOnly;

            char[] trimChars = {'+','*'};
            gameName = parts[0].TrimEnd(trimChars);
            groupName = parts[1].TrimEnd(trimChars);

            logger.Verbose($"{(ModeName())}: _ParseGameAndGroup() GameName: {gameName} ({gameCreateMode}), groupName: {groupName} ({groupCreateMode})");
        }

        private void _CreateGroup()
        {
            logger.Verbose($"{(ModeName())}: _CreateGroup()");
            groupName = groupName ?? "BEAMGRP" + System.Guid.NewGuid().ToString();
            game.apian.CreateNewGroup(groupName);
        }

        private void _JoinGroup()
        {
            BeamPlayer mb = new BeamPlayer(appl.LocalPeer.PeerId, appl.LocalPeer.Name);
            //try {
                game.apian.JoinGroup(groupName, mb.ApianSerialized());
            //} catch (Exception ex) {
            //    _SetState(kFailed, ex.Message);
            //}
        }

        // Event handlers

        public void OnGameCreatedEvt(object sender, string newGameName)
        {
            logger.Info($"{(ModeName())} - OnGameCreatedEvt(): {newGameName}");
            if (_curState == kCreatingGame)
                _SetState(kJoiningGame, newGameName);
            else
                logger.Error($"{(ModeName())} - OnGameCreatedEvt() - Wrong state: {_curState}");
        }


        public void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs ga)
        {
            BeamNetworkPeer p = ga.peer;
            bool isLocal = p.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPeerJoinedGameEvt() - {(isLocal?"Local":"Remote")} Peer Joined: {p.Name}, ID: {p.PeerId}");
            if (isLocal)
            {
                if (_curState == kJoiningGame)
                {
                    // Create gameinstance and ApianInstance
                    game = new BeamAppCore(appl.frontend);
                    game.PlayerJoinedEvt += OnPlayerJoinedEvt;
                    game.NewBikeEvt += OnNewBikeEvt;
                    BeamApian apian = new BeamApianCreatorServer(appl.gameNet, game); // TODO: make the groupMgr type run-time spec'ed
                    //BeamApian apian = new BeamApianSinglePeer(core.gameNet, game); // *** This should be commented out (or gone)
                    appl.AddAppCore(game);
                    _SetState(kCheckingForGroups, null);
                }
                else
                    logger.Error($"{(ModeName())} - OnGameJoinedEvt() - Wrong state: {_curState}");
            }
        }

        public void OnGroupAnnounceEvt(object sender, ApianGroupInfo groupInfo)
        {
            logger.Verbose($"{(ModeName())} - OnGroupAnnounceEvt(): {groupInfo.GroupName}");
            announcedGroups[groupInfo.GroupName] = groupInfo;
        }


        public void OnPlayerJoinedEvt(object sender, PlayerJoinedArgs ga)
        {
            bool isLocal = ga.player.PeerId == appl.LocalPeer.PeerId;
            logger.Info($"{(ModeName())} - OnPlayerJoinedEvt() - {(isLocal?"Local":"Remote")} Member Joined: {ga.player.Name}, ID: {ga.player.PeerId}");
            if (ga.player.PeerId == appl.LocalPeer.PeerId)
            {
                game.RespawnPlayerEvt += OnRespawnPlayerEvt;
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
                game.PostBikeCommand(newBike, BikeCommand.kGo);
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
            BaseBike bb =  game.CreateBaseBike( BikeFactory.AiCtrl, game.LocalPeerId, BikeDemoData.RandomName(), BikeDemoData.RandomTeam());
            game.PostBikeCreateData(bb); // will result in OnBikeInfo()
            logger.Debug($"{this.ModeName()}: SpawnAiBike({ bb.bikeId})");
            return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
        }

        protected string SpawnPlayerBike()
        {
            if (settings.localPlayerCtrlType != "none")
            {
                BaseBike bb =  game.CreateBaseBike( settings.localPlayerCtrlType, game.LocalPeerId, game.LocalPlayer.Name, BikeDemoData.RandomTeam());
                game.PostBikeCreateData(bb); // will result in OnBikeInfo()
                logger.Debug($"{this.ModeName()}: SpawnPlayerBike({ bb.bikeId})");
                return bb.bikeId;  // the bike hasn't been added yet, so this id is not valid yet.
            }
            return null;
        }



    }
}


