using System.Net.WebSockets;
using System;
using System.Text;
using System.Security.Cryptography; // for MD5 hash
using System.Collections.Generic;
using Apian;
using Newtonsoft.Json;
using UnityEngine;
using UniLog;

namespace BeamGameCode
{
    public class BeamApianPeer : ApianGroupMember
    {
        public BeamApianPeer(string _p2pId, string _appHelloData) : base(_p2pId, _appHelloData) { }
    }

    public class BeamApianFactory
    {
        public static BeamApian Create(string apianGroupType, IBeamGameNet beamGameNet, BeamAppCore appCore)
        {
            BeamApian result;
            switch (apianGroupType)
            {
            case SinglePeerGroupManager.kGroupType:
                result = new BeamApianSinglePeer(beamGameNet, appCore);
                break;
            case LeaderSezGroupManager.kGroupType:
                result =  new BeamApianCreatorServer(beamGameNet, appCore);
                break;
            default:
                UniLogger.GetLogger("Apian").Warn($"BeamApianFactory.Create() Unknown GroupTYpe: {apianGroupType}");
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
        protected BeamAppCore client;

        protected bool LocalPeerIsActive {get => (GroupMgr != null) && (GroupMgr.LocalMember?.CurStatus == ApianGroupMember.Status.Active); }

        public long SystemTime { get => DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;}  // system clock

        public BeamApian(IBeamGameNet _gn, IBeamAppCore _client) : base(_gn, _client)
        {
            BeamGameNet = _gn;
            client = _client as BeamAppCore;

            ApianClock = new DefaultApianClock(this);
            apianPeers = new Dictionary<string, BeamApianPeer>();

            // Add BeamApian-level ApianMsg handlers here
            // params are:  from, to, apMsg, msSinceSent
            ApMsgHandlers[ApianMessage.CliRequest] = (f,t,m,d) => this.OnApianRequest(f,t,m,d);
            ApMsgHandlers[ApianMessage.CliObservation] = (f,t,m,d) => this.OnApianObservation(f,t,m,d);
            ApMsgHandlers[ApianMessage.CliCommand] = (f,t,m,d) => this.OnApianCommand(f,t,m,d);
            ApMsgHandlers[ApianMessage.GroupMessage] = (f,t,m,d) => this.OnApianGroupMessage(f,t,m,d);
            ApMsgHandlers[ApianMessage.ApianClockOffset] = (f,t,m,d) => this.OnApianClockOffsetMsg(f,t,m,d);

        }

        public long CurrentRunningApianTime()
        {
            return ApianClock == null ? SystemTime : ApianClock.CurrentTime;
        }


        public override bool Update()
        {
            // Returns TRUE is local peer is "active"
            GroupMgr?.Update();
            ApianClock?.Update();
            return GroupMgr?.LocalMember?.CurStatus == ApianGroupMember.Status.Active;
        }

        protected void AddApianPeer(string p2pId, string peerHelloData)
        {
            BeamApianPeer p = new BeamApianPeer(p2pId, peerHelloData);
            apianPeers[p2pId] = p;
        }

        // Send/Handle ApianMessages

        public override void OnApianMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            ApMsgHandlers[msg.MsgType](fromId, toId, msg, lagMs);
        }

        // Called FROM GroupManager
        public override void OnGroupMemberJoined(ApianGroupMember member) // ATM Beam doesn't care
        {
            base.OnGroupMemberJoined(member);

            // Only care about local peer's group membership status
            if (member.PeerId == GameNet.LocalP2pId())
            {
                client.OnGroupJoined(GroupMgr.GroupId);
            }
        }
        public override void OnGroupMemberStatusChange(ApianGroupMember member, ApianGroupMember.Status prevStatus)
        {
            Logger.Info($"OnGroupMemberStatusChange(): {member.PeerId} went from {prevStatus} to {member.CurStatus}");

            // Beam-specific handling.
            // Joining->Active : PlayerJoined
            // Active->Removed : PlayerLeft
            // TODO: Deal with "missing" (future work)
            switch(prevStatus)
            {
            case ApianGroupMember.Status.Joining:
                if (member.CurStatus == ApianGroupMember.Status.Active)
                {
                    // This is the criterion for a player join
                    // NOTE: currently GroupMgr cant send this directly because BeamPlayerJoined is a BEAM message
                    // and the GroupMgrs don;t know about beam stuff. THIS INCLUDEs PLAYER JOIN CRITERIA!
                    // TODO: This is really awkward because:
                    //      a) We can't have the group manager isntances knowing about the client app
                    //      b) we can't create a command without a "next sequence number")

                    // Try this: it's an OBSERVATION! So it'll get routed to GroupMgr, which will send a command
                    // when appropriate.
                    SendNewPlayerObs(ApianClock.CurrentTime, BeamPlayer.FromApianJson(member.AppDataJson));
                }
                break;
            case ApianGroupMember.Status.Syncing:
                if (member.CurStatus == ApianGroupMember.Status.Active)
                {
                    SendNewPlayerObs(ApianClock.CurrentTime, BeamPlayer.FromApianJson(member.AppDataJson));
                }
                break;
            case ApianGroupMember.Status.Active:
                if (member.CurStatus == ApianGroupMember.Status.Removed)
                    SendPlayerLeftObs(ApianClock.CurrentTime, member.PeerId);
                break;
            }
        }


        private void _AdvanceStateTo(long newApianTime)
        {
            if (GroupMgr?.LocalMember?.CurStatus == ApianGroupMember.Status.Active)
                return; // If peer is active and using the real clock and advancing its own state, dont do anything.

            long curFrameTime = client.FrameApianTime; // previous frame Time

            // TODO: come up with better way to set nominal frame advance time
            long msPerLoop = 40; // 40 ms == 25 fps
            long loops = (newApianTime - curFrameTime) / msPerLoop; // there will be some time left
            for (int i=0;i<loops;i++)
            {
                curFrameTime += msPerLoop;
                client.UpdateFrameTime(curFrameTime);
                client.CoreData.Loop( client.FrameApianTime, msPerLoop);
            }

            if (newApianTime > client.FrameApianTime)
            {
                long msLeft =  newApianTime-client.FrameApianTime;
                client.UpdateFrameTime(newApianTime);
                client.CoreData.Loop(newApianTime, msLeft);
            }
        }

        public override void  ApplyCheckpointStateData(long seqNum, long timeStamp, string stateHash, string stateData)
        {
            client.ApplyCheckpointStateData( seqNum,  timeStamp,  stateHash,  stateData);
        }

        public override void ApplyStashedApianCommand(ApianCommand cmd)
        {
            if (GroupMgr?.LocalMember?.CurStatus != ApianGroupMember.Status.Active
                && cmd.CliMsgType == ApianMessage.CheckpointMsg )
                return; // TODO: &&&& YIKES! See OnApianCommand for relevant comment


            Logger.Info($"BeamApian.ApplyApianCommand() Group: {cmd.DestGroupId}, Applying STASHED Seq#: {cmd.SequenceNum} Type: {cmd.ClientMsg.MsgType} TS: {cmd.ClientMsg.TimeStamp}");
            _AdvanceStateTo((cmd as ApianWrappedClientMessage).ClientMsg.TimeStamp);
            //CommandHandlers[cmd.ClientMsg.MsgType](cmd, ApianGroup.GroupCreatorId, GroupId);

            if (cmd.CliMsgType == ApianMessage.CheckpointMsg ) // TODO: More chekpoint command nonsense.
                client.OnCheckpointCommand(cmd.SequenceNum, cmd.ClientMsg.TimeStamp);
            else
                client.OnApianCommand(cmd);
        }

        // Incoming ApianMessage handlers
        public void OnApianClockOffsetMsg(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            Logger.Verbose($"OnApianClockOffsetMsg(): from {fromId}");
            ApianClock?.OnApianClockOffset(fromId, (msg as ApianClockOffsetMsg).ClockOffset);
        }

        public void OnApianGroupMessage(string fromId, string toId, ApianMessage msg, long lagMs)
        {
            Logger.Debug($"OnApianGroupMessage(): {((msg as ApianGroupMessage).GroupMsgType)}");
            GroupMgr.OnApianMessage(msg, fromId, toId);
        }

        protected void OnApianRequest(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            GroupMgr.OnApianRequest(msg as ApianRequest, fromId, toId);
        }

        protected void OnApianObservation(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            GroupMgr.OnApianObservation(msg as ApianObservation, fromId, toId);
        }

       protected void OnApianCommand(string fromId, string toId, ApianMessage msg, long delayMs)
        {
            ApianCommand cmd = msg as ApianCommand;
            ApianCommandStatus cmdStat = GroupMgr.EvaluateCommand(cmd, fromId, toId);

            switch (cmdStat)
            {
            case ApianCommandStatus.kLocalPeerNotReady:
                Logger.Warn($"BeamApian.OnApianCommand(): Local peer not a group member yet");
                break;
            case ApianCommandStatus.kShouldApply:
                Logger.Verbose($"BeamApian.OnApianCommand() Group: {cmd.DestGroupId}, Applying Seq#: {cmd.SequenceNum} Type: {cmd.ClientMsg.MsgType}");
                if (cmd.CliMsgType == ApianMessage.CheckpointMsg)
                    client.OnCheckpointCommand(cmd.SequenceNum, cmd.ClientMsg.TimeStamp); // TODO: resolve "special-case-hack vs. CheckpointCommand needs to be a real command" issue
                else
                    client.OnApianCommand(cmd);
                break;
            case ApianCommandStatus.kStashedInQueued:
                Logger.Verbose($"BeamApian.OnApianCommand() Group: {cmd.DestGroupId}, Stashing Seq#: {cmd.SequenceNum} Type: {cmd.ClientMsg.MsgType}");
                break;
            case ApianCommandStatus.kAlreadyReceived:
                Logger.Error($"BeamApian.OnApianCommand(): Command Already Received: {fromId} Group: {cmd.DestGroupId}, Seq#: {cmd.SequenceNum} Type: {cmd.ClientMsg.MsgType}");
                break;
            default:
                Logger.Error($"BeamApian.OnApianCommand(): BAD COMMAND SOURCE: {fromId} Group: {cmd.DestGroupId}, Seq#: {cmd.SequenceNum} Type: {cmd.ClientMsg.MsgType}");
                break;

            }
        }

        // State checkpoints

        static string GetMd5Hash(MD5 md5Hash, string input)
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
                string hash = GetMd5Hash(md5Hash, serializedState);
                Logger.Verbose($"SendStateCheckpoint(): SeqNum: {seqNum}, Hash: {hash}");
                GroupMgr.OnLocalStateCheckpoint(seqNum, timeStamp, hash, serializedState);

                GroupCheckpointReportMsg rpt = new GroupCheckpointReportMsg(GroupMgr.GroupId, seqNum, timeStamp, hash);
                BeamGameNet.SendApianMessage(GroupMgr.GroupId, rpt);
            }
        }

        public void SendNewPlayerObs(long timeStamp, BeamPlayer newPlayer)
        {
            Logger.Debug($"SendPlaceHitObs()");
            NewPlayerMsg msg = new NewPlayerMsg(timeStamp, newPlayer);
            ApianNewPlayerObservation obs = new ApianNewPlayerObservation(GroupMgr?.GroupId, msg);
            SendObservation( obs);
        }

        public void SendPlayerLeftObs( long timeStamp, string peerId)
        {
            Logger.Debug($"SendPlayerLeftObs()");
            PlayerLeftMsg msg = new PlayerLeftMsg(timeStamp, peerId);
            ApianPlayerLeftObservation obs = new ApianPlayerLeftObservation(GroupMgr?.GroupId, msg);
            SendObservation( obs);
        }

        public  void SendPlaceClaimObs(long timeStamp, IBike bike, int xIdx, int zIdx,
            Heading entry, Heading exit, Dictionary<string,int> scoreUpdates)
        {
            Logger.Debug($"SendPlaceClaimObs()");
            PlaceClaimMsg msg = new PlaceClaimMsg(timeStamp, bike.bikeId, bike.peerId, xIdx, zIdx, entry, exit, scoreUpdates);
            ApianPlaceClaimObservation obs = new ApianPlaceClaimObservation(GroupMgr?.GroupId, msg);
            SendObservation(obs);
        }

        public void SendPlaceHitObs(long timeStamp, IBike bike, int xIdx, int zIdx, Heading entry, Heading exit, Dictionary<string,int> scoreUpdates)
        {
            Logger.Debug($"SendPlaceHitObs()");
            PlaceHitMsg msg = new PlaceHitMsg(timeStamp, bike.bikeId, bike.peerId, xIdx, zIdx, entry, exit, scoreUpdates);
            ApianPlaceHitObservation obs = new ApianPlaceHitObservation(GroupMgr?.GroupId, msg);
            SendObservation(obs);
        }
        public  void SendRemoveBikeObs(long timeStamp, string bikeId)
        {
            Logger.Info($"SendRemoveBikeObs()");
            RemoveBikeMsg msg = new RemoveBikeMsg(timeStamp, bikeId);
            ApianRemoveBikeObservation obs = new ApianRemoveBikeObservation(GroupMgr?.GroupId, msg);
            SendObservation(obs);
        }

        public void SendPlaceRemovedObs(long timeStamp, int xIdx, int zIdx)
        {
            Logger.Debug($"SendPlaceRemovedObs()");
            PlaceRemovedMsg msg = new PlaceRemovedMsg(timeStamp, xIdx, zIdx);
            ApianPlaceRemovedObservation obs = new ApianPlaceRemovedObservation(GroupMgr?.GroupId, msg);
            SendObservation(obs);
        }

        public  void SendBikeTurnReq(long timeStamp, IBike bike, TurnDir dir, Vector2 nextPt)
        {
            Logger.Debug($"SendBikeTurnReq) Bike: {bike.bikeId}");
            BikeTurnMsg msg = new BikeTurnMsg(timeStamp, bike, dir, nextPt);
            ApianBikeTurnRequest req = new ApianBikeTurnRequest(GroupMgr?.GroupId, msg);
            SendRequest(GroupMgr.GroupId, req);
        }
        public  void SendBikeCommandReq(long timeStamp, IBike bike, BikeCommand cmd, Vector2 nextPt)
        {
            Logger.Debug($"BeamGameNet.SendBikeCommand() Bike: {bike.bikeId}");
            BikeCommandMsg msg = new BikeCommandMsg(timeStamp, bike.bikeId, bike.peerId, cmd, nextPt);
            ApianBikeCommandRequest req = new ApianBikeCommandRequest(GroupMgr?.GroupId, msg);
            SendRequest(GroupMgr.GroupId, req);
        }
        public  void SendBikeCreateReq(long timeStamp, IBike ib)
        {
            Logger.Debug($"SendBikeCreateReq()");
            // Broadcast this to send it to everyone
            BikeCreateDataMsg msg = new BikeCreateDataMsg(timeStamp, ib);
            ApianBikeCreateRequest req = new ApianBikeCreateRequest(GroupMgr.GroupId, msg);
            SendRequest(GroupMgr.GroupId, req);
        }


    }


}