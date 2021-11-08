using System.Net.WebSockets;
using System;
using System.Text;
using System.Security.Cryptography; // for MD5 hash
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
        public BeamApianPeer(string _p2pId, string _appHelloData) : base(_p2pId, _appHelloData) { }
    }

    public static class BeamApianFactory
    {
        public static readonly List<string> ApianGroupTypes = new List<string>()
        {
            SinglePeerGroupManager.kGroupType,
            CreatorSezGroupManager.kGroupType,
            LeaderSezGroupManager.kGroupType
        };

        public static BeamApian Create(string apianGroupType, IBeamGameNet beamGameNet, BeamAppCore appCore)
        {
            BeamApian result;
            switch (apianGroupType)
            {
            case SinglePeerGroupManager.kGroupType:
                result = new BeamApianSinglePeer(beamGameNet, appCore);
                break;
            case CreatorSezGroupManager.kGroupType:
                result =  new BeamApianCreatorSez(beamGameNet, appCore);
                break;
            case LeaderSezGroupManager.kGroupType:
                result =  new BeamApianLeaderSez(beamGameNet, appCore);
                break;
            default:
                UniLogger.GetLogger("Apian").Warn($"BeamApianFactory.Create() Unknown GroupType: {apianGroupType}");
                result = null;
                break;
            }
            return result;
        }
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

        public override ApianGroupMember CreateGroupMember(string peerId, string appMemberDataJson)
        {
            return new BeamApianPeer(peerId, appMemberDataJson);
        }

        public override void Update()
        {
            GroupMgr?.Update();
            ApianClock?.Update();

            ((BeamAppCore)AppCore)?.Loop();

        }

        protected void AddApianPeer(string p2pId, string peerHelloData)
        {
            BeamApianPeer p = new BeamApianPeer(p2pId, peerHelloData);
            apianPeers[p2pId] = p;
        }

        public override void OnPeerMissing(string channelId, string p2pId)
        {
            Logger.Warn($"Peer: {SID(p2pId)} is missing!");
            appCore.OnPlayerMissing(channelId, p2pId);
        }

        public override void OnPeerReturned(string channelId, string p2pId)
        {
            Logger.Warn($"Peer: {SID(p2pId)} has returned!");
            appCore.OnPlayerReturned(channelId, p2pId);
        }


        // Called FROM GroupManager
        public override void OnGroupMemberJoined(ApianGroupMember member) // ATM Beam doesn't care
        {
            base.OnGroupMemberJoined(member);

            // Beam (appCore) only cares about local peer's group membership status
            if (member.PeerId == GameNet.LocalP2pId())
            {
                appCore.OnGroupJoined(GroupMgr.GroupId);  // TODO: wait - appCore has OnGroupJoined? SHouldn't know about groups.
            }
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
                    SendNewPlayerObs(ApianClock.CurrentTime, BeamPlayer.FromApianJson(peer.AppDataJson));
                }
                break;
            case ApianGroupMember.Status.Syncing:
                if (peer.CurStatus == ApianGroupMember.Status.Active)
                {
                    // Most common situation
                    SendNewPlayerObs(ApianClock.CurrentTime, BeamPlayer.FromApianJson(peer.AppDataJson));
                }
                break;
            }

            // No matter Wwhat state it was - it's gone now...
            if (peer.CurStatus == ApianGroupMember.Status.Removed)
            {
                SendPlayerLeftObs(ApianClock.CurrentTime, peer.PeerId);
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


        // State checkpoints

        private static string _GetMd5Hash(MD5 md5Hash, string input)
        {
            // Convert the input string to a byte array and compute the hash.
            byte[] data = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string.
            return sBuilder.ToString();
        }

        public override void SendCheckpointState(long timeStamp, long seqNum, string serializedState) // called by client app
        {
            using (MD5 md5Hash = MD5.Create())
            {
                string hash = _GetMd5Hash(md5Hash, serializedState);
                Logger.Verbose($"SendStateCheckpoint(): SeqNum: {seqNum}, Hash: {hash}");
                GroupMgr.OnLocalStateCheckpoint(seqNum, timeStamp, hash, serializedState);

                GroupCheckpointReportMsg rpt = new GroupCheckpointReportMsg(GroupMgr.GroupId, seqNum, timeStamp, hash);
                BeamGameNet.SendApianMessage(GroupMgr.GroupId, rpt);
            }
        }

        public void SendNewPlayerObs(long timeStamp, BeamPlayer newPlayer)
        {
            Logger.Debug($"SendNewPlayerObs()");
            NewPlayerMsg msg = new NewPlayerMsg(timeStamp, newPlayer);
            SendObservation( msg);
        }

        public void SendPlayerLeftObs( long timeStamp, string peerId)
        {
            Logger.Debug($"SendPlayerLeftObs()");
            PlayerLeftMsg msg = new PlayerLeftMsg(timeStamp, peerId);
            SendObservation( msg);
        }

        public  void SendPlaceClaimObs(long timeStamp, IBike bike, int xIdx, int zIdx,
            Heading entry, Heading exit, Dictionary<string,int> scoreUpdates)
        {
            Logger.Debug($"SendPlaceClaimObs()");
            PlaceClaimMsg msg = new PlaceClaimMsg(timeStamp, bike.bikeId, bike.peerId, xIdx, zIdx, entry, exit, scoreUpdates);
            SendObservation(msg);
        }

        public void SendPlaceHitObs(long timeStamp, IBike bike, int xIdx, int zIdx, Heading entry, Heading exit, Dictionary<string,int> scoreUpdates)
        {
            Logger.Debug($"SendPlaceHitObs()");
            PlaceHitMsg msg = new PlaceHitMsg(timeStamp, bike.bikeId, bike.peerId, xIdx, zIdx, entry, exit, scoreUpdates);
            SendObservation(msg);
        }
        public  void SendRemoveBikeObs(long timeStamp, string bikeId)
        {
            Logger.Info($"SendRemoveBikeObs()");
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
            BikeCommandMsg msg = new BikeCommandMsg(timeStamp, bike.bikeId, bike.peerId, cmd, nextPt);
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