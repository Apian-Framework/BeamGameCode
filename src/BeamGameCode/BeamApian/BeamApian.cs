using System.Net.WebSockets;
using System;
using System.Collections.Generic;
using Apian;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;
using static UniLog.UniLogger; // for SID()

namespace BeamGameCode
{
    public class BeamApianPeer : ApianGroupMember
    {
        public BeamApianPeer(string _peerAddr, string _appHelloData, bool isValidator) : base(_peerAddr, _appHelloData, isValidator) { }
    }

    public abstract class BeamApian : ApianBase
    {

        public Dictionary<string, BeamApianPeer> apianPeers;
        public IBeamGameNet BeamGameNet {get; private set;}
        protected BeamAppCore appCore;

        public long SystemTime { get => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;}  // system clock

        protected BeamApian(IBeamGameNet _gn, IBeamAppCore _client) : base(_gn, _client)
        {
            BeamGameNet = _gn;
            appCore = _client as BeamAppCore;

            // ApianClock and GroupMgr are created in the group-manager-specific subclass
            // ie. BeamApianLeaderSez

            apianPeers = new Dictionary<string, BeamApianPeer>();

            // Add BeamApian-level ApianMsg handlers here
            // params are:  from, to, apMsg, msSinceSent

        }

        public long CurrentRunningApianTime()
        {
            return ApianClock == null ? SystemTime : ApianClock.CurrentTime;
        }

        public override ApianGroupMember CreateGroupMember(string peerAddr, string appMemberDataJson, bool isValidator)
        {
            return new BeamApianPeer(peerAddr, appMemberDataJson, isValidator);
        }

        public override void Update()
        {
            base.Update();
            if (!GroupMgr.AppCorePaused)
                ((BeamAppCore)AppCore)?.Loop();
        }

        protected void AddApianPeer(string peerAddr, string peerHelloData, bool isValidator)
        {
            BeamApianPeer p = new BeamApianPeer(peerAddr, peerHelloData, isValidator);
            apianPeers[peerAddr] = p;
        }

        public override void OnPeerMissing(string channelId, string peerAddr)
        {
            Logger.Warn($"Peer: {SID(peerAddr)} is missing!");
            appCore.OnPlayerMissing(channelId, peerAddr);
        }

        public override void OnPeerReturned(string channelId, string peerAddr)
        {
            Logger.Warn($"Peer: {SID(peerAddr)} has returned!");
            appCore.OnPlayerReturned(channelId, peerAddr);
        }


        // Called FROM GroupManager
        public override void OnGroupMemberJoined(ApianGroupMember member) // ATM Beam doesn't care
        {
            base.OnGroupMemberJoined(member);

            // Beam (appCore) only cares about local peer's group membership status
            if (member.PeerAddr == GameNet.LocalPeerAddr())
            {
                appCore.OnGroupJoined(GroupMgr.GroupId);  // TODO: wait - appCore has OnGroupJoined? SHouldn't know about groups.
            }
        }

        public override void OnGroupMemberLeft(ApianGroupMember member)
        {
            // Send to Apian group to get upgraded to a command
            // Note that "Player" is an AppCore thing, GroupMember is an Apian/network thing
            SendPlayerLeftObs(ApianClock.CurrentTime, member.PeerAddr);
            base.OnGroupMemberLeft(member);
        }

        public override void OnGroupMemberStatusChange(ApianGroupMember member, ApianGroupMember.Status prevStatus)
        {
            base.OnGroupMemberStatusChange(member, prevStatus);
            // Note that the member status has already been changed when this is called

            // Beam-specific handling.
            // Joining->Active : PlayerJoined
            // Active->Removed : PlayerLeft

            //  When we see these transitions send an OBSERVATION.
            // The ApianGroup will can then convert it to a Command and distribute that.
            BeamApianPeer peer = member as BeamApianPeer;

            switch(prevStatus)
            {
            case ApianGroupMember.Status.Joining:
                if (peer.CurStatus == ApianGroupMember.Status.Active)
                {
                    // In a leader-based ApianGroup the first peer will probably go stright from Joining to Active
                    if (!peer.IsValidator) {
                        SendNewPlayerObs(ApianClock.CurrentTime, BeamPlayer.FromApianJson(peer.AppDataJson));
                    }
                }
                break;
            case ApianGroupMember.Status.SyncingState:
            case ApianGroupMember.Status.SyncingClock:
                if (peer.CurStatus == ApianGroupMember.Status.Active)
                {
                    // Most common situation
                    if (!peer.IsValidator) {
                        SendNewPlayerObs(ApianClock.CurrentTime, BeamPlayer.FromApianJson(peer.AppDataJson));
                    }
                }
                break;
            }

        }


        private void _AdvanceStateTo(long newApianTime)
        {
            if (LocalPeerIsActive)
                return; // If peer is active and using the real clock and advancing its own state, dont do anything.

            long curFrameTime = appCore.FrameApianTime; // previous frame Time

            // TODO: come up with better way to set nominal frame advance time
            long msPerLoop = 40; // 40 ms == 25 fps
            long loops = (newApianTime - curFrameTime) / msPerLoop; // there will be some time left
            for (int i=0;i<loops;i++)
            {
                curFrameTime += msPerLoop;
                appCore.UpdateFrameTime(curFrameTime);
                appCore.CoreState.Loop( appCore.FrameApianTime, msPerLoop);
            }

            if (newApianTime > appCore.FrameApianTime)
            {
                long msLeft =  newApianTime-appCore.FrameApianTime;
                appCore.UpdateFrameTime(newApianTime);
                appCore.CoreState.Loop(newApianTime, msLeft);
            }
        }

        public override void ApplyStashedApianCommand(ApianCommand cmd)
        {
            _AdvanceStateTo((cmd as ApianWrappedMessage).PayloadTimeStamp);
            base.ApplyStashedApianCommand(cmd);
        }

        public void SendNewPlayerObs(long timeStamp, BeamPlayer newPlayer)
        {
            Logger.Debug($"SendNewPlayerObs()");
            NewPlayerMsg msg = new NewPlayerMsg(timeStamp, newPlayer);
            SendObservation( msg);
        }

        public void SendPlayerLeftObs( long timeStamp, string peerAddr)
        {
            Logger.Debug($"SendPlayerLeftObs()");
            PlayerLeftMsg msg = new PlayerLeftMsg(timeStamp, peerAddr);
            SendObservation( msg);
        }

        public  void SendPlaceClaimObs(long timeStamp, IBike bike, int xIdx, int zIdx,
            Heading entry, Heading exit, Dictionary<string,int> scoreUpdates)
        {
            Logger.Debug($"SendPlaceClaimObs()");
            PlaceClaimMsg msg = new PlaceClaimMsg(timeStamp, bike.bikeId, bike.playerAddr, xIdx, zIdx, entry, exit, scoreUpdates);
            SendObservation(msg);
        }

        public void SendPlaceHitObs(long timeStamp, IBike bike, int xIdx, int zIdx, Heading entry, Heading exit, Dictionary<string,int> scoreUpdates)
        {
            Logger.Debug($"SendPlaceHitObs()");
            PlaceHitMsg msg = new PlaceHitMsg(timeStamp, bike.bikeId, bike.playerAddr, xIdx, zIdx, entry, exit, scoreUpdates);
            SendObservation(msg);
        }
        public  void SendRemoveBikeObs(long timeStamp, string bikeId)
        {
            Logger.Debug($"SendRemoveBikeObs()");
            RemoveBikeMsg msg = new RemoveBikeMsg(timeStamp, bikeId);
            SendObservation(msg);
        }

        public void SendPlaceRemovedObs(long timeStamp, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceRemovedObs()");
            PlaceRemovedMsg msg = new PlaceRemovedMsg(timeStamp, xIdx, zIdx);
            SendObservation(msg);
        }

        public  void SendBikeTurnReq(long timeStamp, IBike bike, TurnDir dir, Vector2 nextPt)
        {
            Logger.Debug($"SendBikeTurnReq) Bike: {SID(bike.bikeId)}");
            BikeTurnMsg msg = new BikeTurnMsg(timeStamp, bike, dir, nextPt);
            SendRequest(msg);
        }
        public  void SendBikeCommandReq(long timeStamp, IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            Logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {SID(bike.bikeId)}");
            BikeCommandMsg msg = new BikeCommandMsg(timeStamp, bike.bikeId, bike.playerAddr, cmd, nextPt);
            SendRequest(msg);
        }
        public  void SendBikeCreateReq(long timeStamp, IBike ib)
        {
            Logger.Debug($"SendBikeCreateReq()");
            BikeCreateMsg msg = new BikeCreateMsg(timeStamp, ib);
             SendRequest(msg);
        }



    }


}