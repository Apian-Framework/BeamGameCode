using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeamGameCode
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
        void SetAppCore(IBeamAppCore core); // TODO: This is not really what we want

        BeamUserSettings GetUserSettings();

        // Game Modes
        void OnStartMode(int modeId, object param = null);
        void OnEndMode(int modeId, object param = null);

        // Players
        void OnPeerJoinedGameEvt(object sender, PeerJoinedGameArgs pa);
        void OnPeerLeftGameEvt(object sender, PeerLeftGameArgs pa);
        void OnPlayersClearedEvt(object sender, EventArgs e);
        // Bikes
        void OnNewBikeEvt(object sender, IBike ib);
        void OnBikeRemovedEvt(object sender, BikeRemovedData data);
        void OnBikesClearedEvt(object sender, EventArgs e);
        void OnPlaceClaimedEvt(object sender, BeamPlace place);
        // Places
        void OnPlaceHitEvt(object sender, PlaceHitArgs args);
        // scoring
        // void OnScoreEvent(string bikeId, ScoreEvent evt, Ground.Place place); Need this?

        // Ground events
        void OnPlaceFreedEvt(object sender, BeamPlace p);
        void OnSetupPlaceMarkerEvt(object sender, BeamPlace p);
        void OnPlacesClearedEvt(object sender, EventArgs e);

        // Game Events
        void OnReadyToPlay(object sender, EventArgs e);

    }

}
