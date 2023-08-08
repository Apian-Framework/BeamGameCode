using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using ModalApplication;
using UniLog;
using static UniLog.UniLogger; // for SID()
using P2pNet; // just for PeerClockSuncInfo. Kind alame.
using GameNet;
using Apian;
using ApianCrypto;

using System.Threading.Tasks;


namespace BeamGameCode
{
    public class BeamApplication : ILoopingApp, IBeamApplication
    {
        public event EventHandler<PeerJoinedEventArgs> PeerJoinedEvt;
        public event EventHandler<JoinRejectedEventArgs> JoinRejectedEvt;
        public event EventHandler<PeerLeftEventArgs> PeerLeftEvt;
        public event EventHandler<GameAnnounceEventArgs> GameAnnounceEvt;
        public event EventHandler<GameSelectedEventArgs> GameSelectedEvent;
        public event EventHandler<ChainAccountBalanceEventArgs> ChainAccountBalanceEvt;
        public event EventHandler<ChainIdEventArgs> ChainIdEvt;
        public event EventHandler<ChainBlockNumberEventArgs> ChainBlockNumberEvt;


        public LoopModeManager modeMgr {get; private set;}
        public IBeamFrontend frontend {get; private set;}
        public BeamNetworkPeer LocalPeer { get; private set; }
        public BeamNetInfo NetInfo { get; private set;}  // BeamApplication keeps this updated as best as possible
        public BeamGameInfo CurrentGame { get; private set; }
        public bool LocalPeerIsCurrentGameValidator {get; private set;}

        public UniLogger Logger;
        public BeamAppCore mainAppCore {get; private set;}


        public BeamApplication(BeamGameNet bgn, IBeamFrontend fe)
        {
            beamGameNet = bgn;
            beamGameNet.AddClient(this);
            frontend = fe;
            Logger = UniLogger.GetLogger("BeamApplication");
            NetInfo = new BeamNetInfo();
            modeMgr = new LoopModeManager(new BeamModeFactory(), this);

            frontend.SetBeamApplication(this);
        }

        // IBeamApplication
        public  IBeamGameNet beamGameNet {get; private set;}
        public void Start(string initialModeName)
        {
            Start( modeMgr.ModeIdForName(initialModeName));
        }
        public void ExitApplication()
        {
            modeMgr.Stop();
        }

        //
        // Tasks initiated by request from game modes
        //


        public async Task SetupCryptoAcctAsync()
        {
            await Task.Run(() => SetupCryptoAcct());
        }

        public string CreateNewPersistentGameAcct(BeamUserSettings userSettings)
        {
            // create a new acct and put 'em in settings
            string keyStoreJson = beamGameNet.SetupNewCryptoAccount("password");
            string addr = beamGameNet.CryptoAccountAddress();
            Logger.Info($"CreateNewPersistentGameAcct() - Created new Eth acct: {addr}");
            userSettings.gameAcctAddr = addr;
            userSettings.gameAcctJSON[addr] = new PersistentAccount(PersistentAccount.AvailTypes.V3Keystore, addr, keyStoreJson).ToJson();
            UserSettingsMgr.Save(userSettings);
            return addr;
        }

        public void SetupCryptoAcct(bool forceTemp = false)
        {
            // Decrypting a save keystore is very slow (some seeconds) so single-peer modes like the
            // splash and practice one should create a temp acct. A fake one from a know private key
            // would work, too - but forceTemp == true is simpler.
            BeamUserSettings userSettings = frontend.GetUserSettings();
            string addr = null;

            if ( userSettings.GetTempSetting("tempAcct")  == "true" || forceTemp )
            {
                // Create/set temp account
                beamGameNet.SetupNewCryptoAccount(); // no password, so no data to save/restore the account is returned
                addr = beamGameNet.CryptoAccountAddress();
                Logger.Info($"SetupCryptoAcct() - Created new TEMPORARY Eth acct: {addr}");

            } else {

                if (string.IsNullOrEmpty(userSettings.gameAcctAddr))
                {
                    // nothing is set. create a new one
                    // TODO: needs to fetch password
                    addr = CreateNewPersistentGameAcct(userSettings);
                } else {
                    // look up the default one
                    if (userSettings.gameAcctJSON.ContainsKey(userSettings.gameAcctAddr))
                    {
                        // Is it already current? (decrypting a keystore takes a lot of work)
                        if ( beamGameNet.CryptoAccountAddress() != userSettings.gameAcctAddr)
                        {
                            PersistentAccount pAcct = PersistentAccount.FromJson(userSettings.gameAcctJSON[userSettings.gameAcctAddr]);

                            // TODO: needs actual password if it's a keystore
                            addr = beamGameNet.RestoreCryptoAccount(pAcct, "password" );
                            Logger.Info( $"SetupCryptoAcct() - Loaded Eth acct: {addr} from settings");
                        }
                        else
                            Logger.Info( $"SetupCryptoAcct() - Acct: {addr} already loaded");
                    } else {
                        throw new Exception($"SetupCryptoAcct(): No serialized account data found for default address {userSettings.gameAcctAddr}");
                    }
                }
            }
        }

        private string  _CurSettingsChainInfoJson()
        {
            BeamUserSettings userSettings = frontend.GetUserSettings();

            if (string.IsNullOrEmpty(userSettings.curBlockchain))
                throw new Exception("_CurSettingsChainInfoJson(): Current Blockchain not set.");

            if (!userSettings.blockchainInfos.ContainsKey(userSettings.curBlockchain))
                throw new Exception($"_CurSettingsChainInfoJson(): No serialized chainInfo for chain {userSettings.curBlockchain}");

            return userSettings.blockchainInfos[userSettings.curBlockchain];
        }

//        public void CreateCryptoInstance()
//         {
//            beamGameNet.CreateCryptoInstance();
//         }

        public void ConnectToChain()
        {
            string chainInfoJson = _CurSettingsChainInfoJson();
            beamGameNet.ConnectToBlockchain(chainInfoJson);
        }

       public void DisconnectFromChain()
        {
            beamGameNet.DisconnectFromBlockchain();

        }

        // These result in  event invocations
        //public void GetChainId() =>  beamGameNet.GetChainId();
        //public void GetChainBlockNumber() =>  beamGameNet.GetChainBlockNumber();
        //public void GetChainAccountBalance(string address) =>  beamGameNet.GetChainAccountBalance(address);


        // Or use the async/await version
        // public async Task ConnectToChainAsync()
        // {
        //     string chainInfoJson = _CurSettingsChainInfoJson();
        //     await beamGameNet.ConnectToBlockchainAsync(chainInfoJson);
        // }

        public async Task<int> GetChainIdAsync() => await beamGameNet.GetChainIdAsync();
        public async Task<int> GetChainBlockNumberAsync() => await beamGameNet.GetChainBlockNumberAsync();
        public async Task<int> GetChainAccountBalanceAsync(string address) => await beamGameNet.GetChainAccountBalanceAsync(address);


        //
        // Beamnet
        //

        // Connect / Join network
        public void SetupNetwork(string netConnectionStr)
        {
            // This is NOT *joining* a network. Just setting up the connection
            // Connect is (for now) synchronous
              beamGameNet.SetupConnection(beamGameNet.CryptoAccountAddress(), netConnectionStr); // TODO: make this less ugly
        }

        // Ask to join a Beam network
        public void JoinBeamNet(string networkName)
        {
            _ResetJoinNetData();
            _CreateLocalPeer();
            beamGameNet.JoinBeamNet(networkName, LocalPeer);
            // Returns via OnPeerJoinedNetwork()
        }

        // Or use the async/await version
        public async Task<PeerJoinedNetworkData> JoinBeamNetAsync(string networkName)
        {
            _ResetJoinNetData();
            _CreateLocalPeer();  // reads stuff from settings  and p2p instance
            return await beamGameNet.JoinBeamNetAsync(networkName, LocalPeer); // this ends up calling OnPeerJoinedNetwork, too.
        }

        public void LeaveNetwork()
        {
            // Leave a joined BeamNet
            beamGameNet.LeaveNetwork();
            _ResetJoinNetData();
        }
        public void TearDownNetwork()
        {
            // Tear down the whole P2pNet thing (opposite of Setup)
            beamGameNet.TearDownConnection();
        }


        // Get a list of active/available Beam game instances on the net
        public void ListenForGames()
        {
            beamGameNet.RequestGroups();
            // Game (Group) announcements come back in OnGroupAnnounce()
            // assumes caller is collecting them and will time out at some point and continue
        }

        // ...or async/await
        public async Task<Dictionary<string, BeamGameAnnounceData>> GetExistingGamesAsync(int waitMs)
        {
            // Does NOT overwrite existing NetInfo
            Dictionary<string, GroupAnnounceResult> _ = await beamGameNet.RequestGroupsAsync(waitMs);

            // also, we need the dict keyed by game name
            return NetInfo.BeamGames.Values.ToDictionary(bgd => bgd.GameInfo.GameName, bgd => bgd);
        }

        // Now that the network is "joined" and there is information regarding how many peers there are
        // and perhaps whether there are games, ask the frontend to notify the now-waiting application
        // whether and when it should proceed to game creation/selection or perhaps cancel and disconnect.

        // While it seems odd to have this break between connecting to the net and starting a game, and even
        // weirder for the frontend to be involved, it turns out that in practice to a user there's a real
        // difference between joining the net and joining a game.

        // ACTUALLY, BeamApplication has nothing to do with this. The GameMoe just calls FE.OnNetworkReady()
        // and the FE changes modes (maybe after waiting a bit...)
        public void OnNetworkReady() => frontend.OnNetworkReady();

        // Select/join game

        // Ask the frontend to either select a game from the given list,
        // ...Or provide the data to create a new one
        // ...or cancel altogether
        public void  SelectGame(IDictionary<string, BeamGameAnnounceData> existingGames)
        {
            frontend.SelectGame(existingGames);
            // frontend displays a selection UI which eventually calls back OnGameSelected()
            // ...or the FE might just immediately call OnGameSelected() itself
        }

       public void OnGameSelected(GameSelectedEventArgs gameSelectArgs)
        {
            Logger.Info($"OnGameSelected({gameSelectArgs.gameInfo?.GameName}): Joining as {(gameSelectArgs.joinAsValidator ? "Validator" : "Player")}  ");
            GameSelectedEvent?.Invoke(this, gameSelectArgs);
        }

        public async Task<GameSelectedEventArgs> SelectGameAsync(IDictionary<string, BeamGameAnnounceData> existingGames)
        {
            GameSelectedEventArgs selection = await frontend.SelectGameAsync(existingGames);
            Logger.Info($"SelectGameAsync() Got result:  GameName: {selection.gameInfo?.GameName} ResultCode: {selection.result}, IsValidator: {selection.joinAsValidator}");

            return selection;
        }

        // Given the specifier for a game to join or create. Join it, or create it and then join it
        // This results on a callback to OnPeerJoinedGroup() with the local peer and the requested game (group)
        //   as parameters, which is the info that the async version return.

        // But what the game code really waits for isntead is a PlayerJoinedEvent sent from the AppCore which will come
        // soon thereafter


        // Used in single-peer play
        public void CreateAndJoinGame(BeamGameInfo gameInfo, BeamAppCore appCore, bool joinAsValidator)
        {
            beamGameNet.CreateAndJoinGame(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized(), joinAsValidator);
        }

        public async Task<LocalPeerJoinedGameData> CreateAndJoinGameAsync(BeamGameInfo gameInfo, BeamAppCore appCore, int timeoutMs, bool joinAsValidator)
        {
            PeerJoinedGroupData joinData = await beamGameNet.CreateAndJoinGameAsync(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized(), timeoutMs,  joinAsValidator);
            return new LocalPeerJoinedGameData(joinData.Success, joinData.GroupInfo.GroupId, joinData.Message);
        }

        public async Task<LocalPeerJoinedGameData> JoinExistingGameAsync(BeamGameInfo gameInfo, BeamAppCore appCore, int timeoutMs, bool joinAsValidator)
        {
            PeerJoinedGroupData joinData = await beamGameNet.JoinExistingGameAsync(gameInfo, appCore?.apian, MakeBeamPlayer().ApianSerialized(), timeoutMs,  joinAsValidator);
            return new LocalPeerJoinedGameData(joinData.Success, joinData.GroupInfo.GroupId, joinData.Message);
        }

        public void LeaveGame()
        {
            if (CurrentGame != null)
            {
                Logger.Info($"LeaveGame() - leaving game {CurrentGame.GroupFriendlyId}");
                beamGameNet.LeaveGame(CurrentGame.GroupId);
                CurrentGame = null;
                LocalPeerIsCurrentGameValidator = false;
            }

        }

        // Game mode  control

        public int CurrentGameModeId() => modeMgr.CurrentModeId();

        public void OnSwitchModeReq(int newModeId, object modeParam)
        {
           modeMgr.SwitchToMode(newModeId, modeParam);
        }

        public void OnPushModeReq(int newModeId, object modeParam)
        {
           modeMgr.PushMode(newModeId, modeParam);
        }
        public void OnPopModeReq(object resultParam)
        {
           modeMgr.PopMode(resultParam);
        }

        //
        // ILoopingApp
        //
        public void Start(int initialMode) // required by loopingapp
        {
          modeMgr.Start(initialMode);
        }
        public void End() {}
        public bool Loop(float frameSecs)
        {
            return modeMgr.Loop(frameSecs);
        }

        //
        // IApianApplication
        //
        public void AddAppCore(IApianAppCore gi)
        {
            // Beam only supports 1 game instance
            mainAppCore = gi as BeamAppCore;
            frontend.SetAppCore(gi as IBeamAppCore); // HACK
        }
        public void OnGroupAnnounce(GroupAnnounceResult groupAnn)
        {
            Logger.Info($"OnGroupAnnounce({groupAnn.GroupInfo.GroupName})");
            BeamGameAnnounceData gd = new BeamGameAnnounceData(groupAnn);
            NetInfo?.AddGame(gd);
            GameAnnounceEvt?.Invoke(this, new GameAnnounceEventArgs(gd));
        }
        public void OnGroupMemberStatus(string groupId, string peerAddr, ApianGroupMember.Status newStatus, ApianGroupMember.Status prevStatus)
        {
            Logger.Info($"OnGroupMemberStatus() Grp: {groupId}, Peer: {UniLogger.SID(peerAddr)}, Status: {newStatus}, Prev: {prevStatus}");
            frontend.OnGroupMemberStatus(groupId, peerAddr, newStatus, prevStatus);
        }
        public void OnPeerJoinedGroup(PeerJoinedGroupData data)
        {
            Logger.Info($"OnPeerJoinedGroup() Grp: {data.GroupInfo.GroupFriendlyId}, Peer: {SID(data.PeerAddr)}, Local: {data?.PeerAddr == LocalPeer.PeerAddr}, Validator: {data.IsValidator}");
            bool isLocalPeer = data?.PeerAddr == LocalPeer.PeerAddr;
            if (isLocalPeer) {
                CurrentGame = data.GroupInfo as BeamGameInfo;
                LocalPeerIsCurrentGameValidator = data.IsValidator;
            }
        }

        public void OnGroupLeaderChange(string groupId, string newLeaderAddr, ApianGroupMember leaderData)
        {
            string lname = leaderData != null ?  BeamPlayer.FromApianJson(leaderData.AppDataJson).Name : null;

            frontend.OnGroupLeaderChanged(groupId, newLeaderAddr, lname);

        }

        public void DisplayError(string message, int status = -1, int param = -1)
        {
            frontend.DisplayMessage(MessageSeverity.Error, message);
        }


        //
        // IGameNetClient
        //
        public void OnPeerJoinedNetwork(PeerJoinedNetworkData peerData)
        {
            BeamNetworkPeer peer = JsonConvert.DeserializeObject<BeamNetworkPeer>(peerData.HelloData);
            bool isLocalPeer = peerData.PeerAddr == LocalPeer.PeerAddr;

            if (isLocalPeer)
                NetInfo = new BeamNetInfo(beamGameNet.CurrentNetworkChannel());
            NetInfo.AddPeer(peer);

            Logger.Info($"OnPeerJoinedNetwork() {(isLocalPeer ? "Local" : "Remote")} name: {peer.Name}");
            PeerJoinedEvt?.Invoke(this, new PeerJoinedEventArgs(peerData.NetId, peer));
        }

        public void OnPeerLeftNetwork(string peerAddr, string netId)
        {
            Logger.Info($"OnPeerLeftNetwork({SID(peerAddr)})");
            NetInfo?.RemovePeer(peerAddr);
            PeerLeftEvt?.Invoke(this, new PeerLeftEventArgs(netId, peerAddr)); // Event instance might be gone
        }

        // Covers both network and group/channels, and probably arrives after OnWhateverJoined() notification
        public void OnJoinRejected(string channelId, string failureReason)
        {
           JoinRejectedEvt?.Invoke(this, new JoinRejectedEventArgs(channelId, failureReason));
        }


        // Apian handles these at the game level. Not sure what would be useful here.
        public void OnPeerMissing(string peerAddr, string netId) { }
        public void OnPeerReturned(string peerAddr, string netId){ }
        public void OnPeerJoinedChannel(string _addr, string _chan, string _data ) {}
        public void OnPeerLeftChannel(string _addr, string _chan ) {} //

        public void OnPeerSync(string channel, string peerAddr, PeerClockSyncInfo syncInfo) {} // stubbed
        // TODO: Be nice to be able to default-stub this somewhere.


        // IApianGameNetClient (chain stuff)
        public void OnChainId(int chainId, Exception ex)
        {
            Logger.Info($"OnChainId({chainId})");
            ChainIdEvt?.Invoke(this, new ChainIdEventArgs(chainId));
        }

        public void OnChainBlockNumber(int blockNumber, Exception ex)
        {
            Logger.Info($"OnChainBlockNumber({blockNumber})");
            ChainBlockNumberEvt?.Invoke(this, new ChainBlockNumberEventArgs(blockNumber));
        }

        public void OnChainAcctBalance(string addr, int balance, Exception ex)
        {
            Logger.Info($"OnChainAcctBalance() Addr: {addr}, Balance: {balance}");
            ChainAccountBalanceEvt?.Invoke(this, new ChainAccountBalanceEventArgs(addr, balance));
        }

        public void OnSessionRegistered(string sessId, string txHash, Exception ex)
        {
            if (ex != null)
            {
                frontend.DisplayMessage(MessageSeverity.Error, ex.Message + (ex.InnerException==null?"":$" [{ex.InnerException}]"));
                throw ex;
            }
            Logger.Info($"OnSessionRegistration() Session: {sessId},  TxHash: {txHash}");
        }

        public void OnEpochReported(string sessId, long epochNum, string txHash, Exception ex)
        {
            if (ex != null)
            {
                frontend.DisplayMessage(MessageSeverity.Error, ex.Message + (ex.InnerException==null?"":$" [{ex.InnerException}]"));
                throw ex;
            }
            Logger.Info($"OnEpochReported() Session: {sessId}, Epoch #{epochNum} TxHash: {txHash}");
        }

        // Utility methods

        private void _CreateLocalPeer()
        {
            BeamUserSettings settings = frontend.GetUserSettings();
            LocalPeer = new BeamNetworkPeer(beamGameNet.LocalPeerAddr(), settings.screenName); // must have called
            if (LocalPeer.PeerAddr == null)
                throw new ArgumentNullException("LocalPeer.PeerAddr"); // ConnectToNetwork() not called/failed?

        }

        private void _ResetJoinNetData()
        {
            NetInfo = new BeamNetInfo(); // clear it all.
            LocalPeer = null;
        }

        protected BeamPlayer MakeBeamPlayer() => new BeamPlayer(LocalPeer.PeerAddr, LocalPeer.Name);
        // FIXME: I think maybe it should go in BeamGameNet?

        public BaseBike CreateBaseBike(string ctrlType, string peerAddr, string name, Team t)
        {
            Heading heading = BikeFactory.PickRandomHeading();
            Vector2 pos = BikeFactory.PositionForNewBike( mainAppCore.CoreState, mainAppCore.CurrentRunningGameTime, heading, Ground.zeroPos, Ground.gridSize * 10 );
            string bikeId = Guid.NewGuid().ToString("N");
            return  new BaseBike(mainAppCore.CoreState, bikeId, peerAddr, name, t, ctrlType, mainAppCore.CurrentRunningGameTime, pos, heading);
        }

    }
}