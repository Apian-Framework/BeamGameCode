﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamBackend
{
    public class TargetIdParams {public string targetId;}   
        
    public interface IFrontendModeHelper 
    {
        void OnStartMode(int modeId, object param);
        void DispatchCmd(int modeId, int cmdId, object param);
        void OnEndMode(int modeId, object param);
    }


    public interface IBeamFrontend 
    {
        // Called by backend

        BeamUserSettings GetUserSettings();

        // Game Modes
        void OnStartMode(int modeId, object param = null);
        void OnEndMode(int modeId, object param = null);        

        // Players
        void OnPeerJoinedEvt(object sender, BeamPeer p);
        void OnPeerLeftEvt(object sender, string p2pId);
        void OnPeersClearedEvt(object sender, EventArgs e);        
        // Bikes
        void OnNewBikeEvt(object sender, IBike ib);
        void OnBikeRemovedEvt(object sender, BikeRemovedData data);
        void OnBikesClearedEvt(object sender, EventArgs e);


        void OnPlaceClaimedEvt(object sender, Ground.Place place);
        // Places
        void OnPlaceHitEvt(object sender, PlaceHitArgs args);    
        void OnPlaceFreedEvt(object sender, Ground.Place p);     
        void OnPlacesClearedEvt(object sender, EventArgs e);
        // scoring
        // void OnScoreEvent(string bikeId, ScoreEvent evt, Ground.Place place); Need this?
    }

}
