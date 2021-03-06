﻿using System;
using Apian;

namespace BeamGameCode
{
    //
    // Event args
    //
    public struct PlayerJoinedArgs {
        public string groupChannel;
        public BeamPlayer player;
        public PlayerJoinedArgs(string g, BeamPlayer p) {groupChannel=g; player=p;}
    }
    public struct PlayerLeftArgs {
        public string groupChannel;
        public string p2pId;
        public PlayerLeftArgs(string g, string p) {groupChannel=g; p2pId=p;}
    }

    public struct BikeRemovedData {
        public string bikeId;
        public bool doExplode;
        public BikeRemovedData(string i, bool b) {bikeId=i; doExplode=b;}
    }

    public struct PlaceHitArgs
    {
        public BeamPlace p;
        public IBike ib;
        public PlaceHitArgs(BeamPlace _p, IBike _ib) { p=_p; ib=_ib; }
    }


    public interface IBeamAppCore : IApianAppCore
    {
        // API for application code

        // Events
        event EventHandler<string> GroupJoinedEvt;
        event EventHandler<BeamCoreState> NewCoreStateEvt;
        event EventHandler<PlayerJoinedArgs> PlayerJoinedEvt;
        event EventHandler<PlayerLeftArgs> PlayerLeftEvt;
        event EventHandler<PlayerLeftArgs> PlayerMissingEvt; // not Gone... yet
        event EventHandler<PlayerLeftArgs> PlayerReturnedEvt;
        event EventHandler PlayersClearedEvt;
        event EventHandler<IBike> NewBikeEvt;
        event EventHandler<BikeRemovedData> BikeRemovedEvt;
        event EventHandler BikesClearedEvt;
        event EventHandler<BeamPlace> PlaceClaimedEvt;
        event EventHandler<PlaceHitArgs> PlaceHitEvt;
        event EventHandler<string> UnknownBikeEvt;

        // Instigated by game mode code
        event EventHandler ReadyToPlayEvt;
        event EventHandler RespawnPlayerEvt;
		void RaiseReadyToPlay();
		void RaiseRespawnPlayer();

        // Access
        Ground GetGround();

        string LocalPeerId {get;}
        BeamCoreState CoreState {get;}

        string ApianNetId {get;}
        string ApianGroupName {get;}
        string ApianGroupId {get;}
        long CurrentRunningGameTime {get;}



    }
}
