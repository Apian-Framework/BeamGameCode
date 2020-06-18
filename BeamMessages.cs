using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Apian;

namespace BeamBackend
{
    public class BeamMessage : ApianClientMsg
    {
        public const string kNewPlayer = "Bpln";
        public const string kPlayerLeft = "Bpll";
        public const string kBikeCreateData = "Bbcd";
        public const string kRemoveBikeMsg = "Bbrm";
        public const string kBikeDataQuery = "Bbdq"; // TODO: check if still exists/used
        public const string kBikeTurnMsg = "Btrn";
        public const string kBikeCommandMsg = "Bcmd";
        public const string kPlaceClaimMsg = "Bplc";
        public const string kPlaceHitMsg = "Bplh";
        public const string kPlaceRemovedMsg = "Bplr";

        // Data classes
        public class BikeState
        {
            public int score;
            public float xPos;
            public float yPos;
            public Heading heading;
            public float speed;

            public BikeState() {}

            public BikeState(IBike ib)
            {
                score = ib.score;
                xPos = ib.position.x;
                yPos = ib.position.y;
                heading = ib.heading;
                speed = ib.speed;
            }

            public BikeState(int _score, float _xPos, float _yPos, Heading _heading, float _speed)
            {
                score = _score;
                xPos = _xPos;
                yPos = _yPos;
                heading = _heading;
                speed = _speed;
            }
        }

          public class PlaceCreateData // Don't need bikeId since this is part of a bike data msg
        {
            public int xIdx;
            public int zIdx;
            public long expireTimeMs;

            public PlaceCreateData() {} // need a default ctor to deserialize
            public PlaceCreateData(BeamPlace p)
            {
                xIdx = p.xIdx;
                zIdx = p.zIdx;
                expireTimeMs = p.expirationTimeMs;
            }
        }

        public BeamMessage(string t, long ts) : base(t,ts) {}
        public BeamMessage() : base() {}
    }


    //
    // GameNet messages
    //
    //

    public class NewPlayerMsg : BeamMessage
    {
        public BeamPlayer newPlayer;
        public NewPlayerMsg(long ts, BeamPlayer _newPlayer) : base(kNewPlayer, ts) => newPlayer = _newPlayer;
        public NewPlayerMsg() : base() {}
    }

    // BeamApian sees a GroupMember change to active and creates an "observation" and send it to the
    // GroupManager (the GroupManager doesn;t know what a BeamPlayer is, or what the criteria for a new one is - but it
    // DOES know whether or not it should send out a submitted observation as a Command)
    public class ApianNewPlayerObservation : ApianObservation
    {
        public override ApianClientMsg ClientMsg {get => newPlayerMsg;}
        public NewPlayerMsg newPlayerMsg;
        public ApianNewPlayerObservation(string gid, NewPlayerMsg _newPlayerMsg) : base(gid, _newPlayerMsg) {newPlayerMsg=_newPlayerMsg;}
        public ApianNewPlayerObservation() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianNewPlayerCommand(seqNum, DestGroupId, newPlayerMsg);
    }
    public class ApianNewPlayerCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => newPlayerMsg;}
        public NewPlayerMsg newPlayerMsg;
        public ApianNewPlayerCommand(long seqNum, string gid, NewPlayerMsg _newPlayerMsg) : base(seqNum, gid, _newPlayerMsg) {newPlayerMsg=_newPlayerMsg;}
        public ApianNewPlayerCommand() : base() {}
    }

    public class PlayerLeftMsg : BeamMessage
    {
        public string peerId;
        public PlayerLeftMsg(long ts, string _peerId) : base(kPlayerLeft, ts) => peerId = _peerId;
        public PlayerLeftMsg() : base() {}
    }

    public class ApianPlayerLeftObservation : ApianObservation
    {
        public override ApianClientMsg ClientMsg {get => playerLeftMsg;}
        public PlayerLeftMsg playerLeftMsg;
        public ApianPlayerLeftObservation(string gid, PlayerLeftMsg _playerLeftMsg) : base(gid, _playerLeftMsg) {playerLeftMsg=_playerLeftMsg;}
        public ApianPlayerLeftObservation() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianPlayerLeftCommand(seqNum, DestGroupId, playerLeftMsg);
    }
    public class ApianPlayerLeftCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => playerLeftMsg;}
        public PlayerLeftMsg playerLeftMsg;
        public ApianPlayerLeftCommand(long seqNum, string gid, PlayerLeftMsg _playerLeftMsg) : base(seqNum, gid, _playerLeftMsg) {playerLeftMsg=_playerLeftMsg;}
        public ApianPlayerLeftCommand() : base() {}
    }

    public class BikeCreateDataMsg : BeamMessage
    {
        public string bikeId;
        public string peerId;
        public string name;
        public Team team;
        public int score;
        public string ctrlType;
        public float xPos;
        public float yPos;
        public Heading heading;

        public BikeCreateDataMsg(long ts, IBike ib) : base(kBikeCreateData, ts)
        {
            bikeId = ib.bikeId;
            peerId = ib.peerId;
            name = ib.name;
            team = ib.team;
            score = ib.score;
            ctrlType = ib.ctrlType;
            xPos = ib.position.x;
            yPos = ib.position.y;
            heading = ib.heading;
        }

        public BikeCreateDataMsg() : base() {}

        public IBike ToBike(BeamGameState gd)
        {
            // Remote bikes always get control type: BikeFactory.RemoteCrtl
            return new BaseBike(gd, bikeId, peerId , name, team, ctrlType, new Vector2(xPos, yPos), heading);
        }
    }

    public class ApianBikeCreateRequest : ApianRequest
    {
        public override ApianClientMsg ClientMsg {get => bikeCreateDataMsg;}
        public BikeCreateDataMsg bikeCreateDataMsg;
        public ApianBikeCreateRequest(string gid, BikeCreateDataMsg _bikeCreateMsg) : base(gid, _bikeCreateMsg) {bikeCreateDataMsg=_bikeCreateMsg;}
        public ApianBikeCreateRequest() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianBikeCreateCommand(seqNum, DestGroupId, bikeCreateDataMsg);
    }

    public class ApianBikeCreateCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => bikeCreateDataMsg;}
        public BikeCreateDataMsg bikeCreateDataMsg;
        public ApianBikeCreateCommand(long seqNum, string gid, BikeCreateDataMsg _bikeCreateMsg) : base(seqNum, gid, _bikeCreateMsg) {bikeCreateDataMsg=_bikeCreateMsg;}
        public ApianBikeCreateCommand() : base() {}

    }

    public class RemoveBikeMsg : BeamMessage
    {
        public string bikeId;
        public RemoveBikeMsg() : base() {}
        public RemoveBikeMsg(long ts, string _bikeId) : base(kRemoveBikeMsg, ts) { bikeId = _bikeId; }
    }

    public class ApianRemoveBikeObservation : ApianObservation
    {
        public override ApianClientMsg ClientMsg {get => removeBikeMsg;}
        public RemoveBikeMsg removeBikeMsg;
        public ApianRemoveBikeObservation(string gid, RemoveBikeMsg _removeBikeMsg) : base(gid, _removeBikeMsg) {removeBikeMsg=_removeBikeMsg;}
        public ApianRemoveBikeObservation() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianRemoveBikeCommand(seqNum, DestGroupId, removeBikeMsg);
    }
    public class ApianRemoveBikeCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => removeBikeMsg;}
        public RemoveBikeMsg removeBikeMsg;
        public ApianRemoveBikeCommand(long seqNum, string gid, RemoveBikeMsg _removeBikeMsg) : base(seqNum, gid, _removeBikeMsg) {removeBikeMsg=_removeBikeMsg;}
        public ApianRemoveBikeCommand() : base() {}
    }

    public class BikeTurnMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;
        public string ownerPeer;
        public BikeState bikeState;
        public TurnDir dir;
        public float nextPtX;
        public float nextPtZ;
        public BikeTurnMsg() : base()  {}

        public BikeTurnMsg(long ts, IBike ib, TurnDir _dir, Vector2 nextGridPt) : base(kBikeTurnMsg, ts)
        {
            bikeId = ib.bikeId;
            ownerPeer = ib.peerId;
            bikeState = new BikeState(ib);
            dir = _dir;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
        }
    }

    public class ApianBikeTurnRequest : ApianRequest
    {
        public override ApianClientMsg ClientMsg {get => bikeTurnMsg;}
        public BikeTurnMsg bikeTurnMsg;
        public ApianBikeTurnRequest(string gid, BikeTurnMsg _bikeTurnMsg) : base(gid, _bikeTurnMsg) {bikeTurnMsg=_bikeTurnMsg;}
        public ApianBikeTurnRequest() : base() {}

        public override ApianCommand ToCommand(long seqNum) => new ApianBikeTurnCommand(seqNum, DestGroupId, bikeTurnMsg);
    }
    public class ApianBikeTurnCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => bikeTurnMsg;}
        public BikeTurnMsg bikeTurnMsg;
        public ApianBikeTurnCommand(long seqNum, string gid, BikeTurnMsg _bikeTurnMsg) : base(seqNum, gid, _bikeTurnMsg) {bikeTurnMsg=_bikeTurnMsg;}
        public ApianBikeTurnCommand() : base() {}
    }

    public class BikeCommandMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;
        public string ownerPeer;
        public BikeCommand cmd;
        public float nextPtX;
        public float nextPtZ;
        public BikeCommandMsg() : base()  {}
        public BikeCommandMsg(long ts, string _bikeId, string _ownerPeer, BikeCommand _cmd, Vector2 nextGridPt) : base(kBikeCommandMsg, ts)
        {
            bikeId = _bikeId;
            ownerPeer = _ownerPeer;
            cmd = _cmd;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
        }
    }

    public class ApianBikeCommandRequest : ApianRequest
    {
        public override ApianClientMsg ClientMsg {get => bikeCommandMsg;}
        public BikeCommandMsg bikeCommandMsg;
        public ApianBikeCommandRequest(string gid, BikeCommandMsg _bikeCommandMsg) : base(gid, _bikeCommandMsg) {bikeCommandMsg=_bikeCommandMsg;}
        public ApianBikeCommandRequest() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianBikeCommandCommand(seqNum, DestGroupId, bikeCommandMsg);
    }

    public class ApianBikeCommandCommand : ApianCommand  // Gee, no - that's not stupid-sounding at all]
    {
        public override ApianClientMsg ClientMsg {get => bikeCommandMsg;}
        public BikeCommandMsg bikeCommandMsg;
        public ApianBikeCommandCommand(long seqNum, string gid, BikeCommandMsg _bikeCommandMsg) : base(seqNum, gid, _bikeCommandMsg) {bikeCommandMsg=_bikeCommandMsg;}
        public ApianBikeCommandCommand() : base() {}
    }

    public class PlaceClaimMsg : BeamMessage
    {
        public string bikeId;
        public string ownerPeer; // this is redundant (get it from the bike)
        public int xIdx;
        public int zIdx;
        public Heading entryHead;
        public Heading exitHead;
        public Dictionary<string,int> scoreUpdates;
        public PlaceClaimMsg() : base() {}
        public PlaceClaimMsg(long ts, string _bikeId, string _ownerPeer, int  _xIdx, Int32 _zIdx,
                            Heading entryH, Heading exitH, Dictionary<string,int> dScores) : base(kPlaceClaimMsg, ts)
        {
            bikeId = _bikeId;
            ownerPeer = _ownerPeer;
            xIdx = _xIdx;
            zIdx = _zIdx;
            entryHead = entryH;
            exitHead = exitH;
            scoreUpdates = dScores;
        }
    }

    public class ApianPlaceClaimObservation : ApianObservation
    {
        public override ApianClientMsg ClientMsg {get => placeClaimMsg;}
        public PlaceClaimMsg placeClaimMsg;
        public ApianPlaceClaimObservation(string gid, PlaceClaimMsg _placeClaimMsg) : base(gid, _placeClaimMsg) {placeClaimMsg=_placeClaimMsg;}
        public ApianPlaceClaimObservation() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianPlaceClaimCommand(seqNum, DestGroupId, placeClaimMsg);
    }

    public class ApianPlaceClaimCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => placeClaimMsg;}
        public PlaceClaimMsg placeClaimMsg;
        public ApianPlaceClaimCommand(long seqNum, string gid, PlaceClaimMsg _placeClaimMsg) : base(seqNum, gid, _placeClaimMsg) {placeClaimMsg=_placeClaimMsg;}
        public ApianPlaceClaimCommand() : base() {}
    }

    public class PlaceHitMsg : BeamMessage
    {
        public string bikeId;
        public string ownerPeer;
        public int xIdx;
        public int zIdx;
        public Heading entryHead;
        public Heading exitHead;
        public Dictionary<string,int> scoreUpdates;
        public PlaceHitMsg() : base() {}
        public PlaceHitMsg(long ts, string _bikeId, string _ownerPeer, int _xIdx, int _zIdx,
            Heading entryH, Heading exitH, Dictionary<string,int> dScores) : base(kPlaceHitMsg, ts)
        {
            bikeId = _bikeId;
            ownerPeer = _ownerPeer;
            xIdx=_xIdx;
            zIdx=_zIdx;
            entryHead = entryH;
            exitHead = exitH;
            scoreUpdates = dScores;
        }
    }

    public class ApianPlaceHitObservation : ApianObservation
    {
        public override ApianClientMsg ClientMsg {get => placeHitMsg;}
        public PlaceHitMsg placeHitMsg;
        public ApianPlaceHitObservation(string gid, PlaceHitMsg _placeHitMsg) : base(gid, _placeHitMsg) {placeHitMsg=_placeHitMsg;}
        public ApianPlaceHitObservation() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianPlaceHitCommand(seqNum, DestGroupId, placeHitMsg);
    }
    public class ApianPlaceHitCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => placeHitMsg;}
        public PlaceHitMsg placeHitMsg;
        public ApianPlaceHitCommand(long seqNum, string gid, PlaceHitMsg _placeHitMsg) : base(seqNum, gid, _placeHitMsg) {placeHitMsg=_placeHitMsg;}
        public ApianPlaceHitCommand() : base() {}
    }

    public class PlaceRemovedMsg : BeamMessage
    {
        public int xIdx;
        public int zIdx;
        public PlaceRemovedMsg() : base() {}
        public PlaceRemovedMsg(long ts, int _xIdx, int _zIdx) : base(kPlaceRemovedMsg, ts)
        {
            xIdx=_xIdx;
            zIdx=_zIdx;
        }
    }

    public class ApianPlaceRemovedObservation : ApianObservation
    {
        public override ApianClientMsg ClientMsg {get => placeRemovedMsg;}
        public PlaceRemovedMsg placeRemovedMsg;
        public ApianPlaceRemovedObservation(string gid, PlaceRemovedMsg _placeRemovedMsg) : base(gid, _placeRemovedMsg) {placeRemovedMsg=_placeRemovedMsg;}
        public ApianPlaceRemovedObservation() : base() {}
        public override ApianCommand ToCommand(long seqNum) => new ApianPlaceRemovedCommand(seqNum, DestGroupId, placeRemovedMsg);
    }
    public class ApianPlaceRemovedCommand : ApianCommand
    {
        public override ApianClientMsg ClientMsg {get => placeRemovedMsg;}
        public PlaceRemovedMsg placeRemovedMsg;
        public ApianPlaceRemovedCommand(long seqNum, string gid, PlaceRemovedMsg _placeRemovedMsg) : base(seqNum, gid, _placeRemovedMsg) {placeRemovedMsg=_placeRemovedMsg;}
        public ApianPlaceRemovedCommand() : base() {}
    }


    static public class BeamApianMessageDeserializer
    {
        // TODO: Come up with a sane way of desrializing messages
        //(prefereably without having to include class type info in the JSON)
        public static Dictionary<string, Func<string, ApianMessage>> beamDeserializers = new  Dictionary<string, Func<string, ApianMessage>>()
        {
            {ApianMessage.CliObservation+BeamMessage.kNewPlayer, (s) => JsonConvert.DeserializeObject<ApianNewPlayerObservation>(s) },
            {ApianMessage.CliObservation+BeamMessage.kPlayerLeft, (s) => JsonConvert.DeserializeObject<ApianPlayerLeftObservation>(s) },
            {ApianMessage.CliRequest+BeamMessage.kBikeTurnMsg, (s) => JsonConvert.DeserializeObject<ApianBikeTurnRequest>(s) },
            {ApianMessage.CliRequest+BeamMessage.kBikeCommandMsg, (s) => JsonConvert.DeserializeObject<ApianBikeCommandRequest>(s) },
            {ApianMessage.CliRequest+BeamMessage.kBikeCreateData, (s) => JsonConvert.DeserializeObject<ApianBikeCreateRequest>(s) },
            {ApianMessage.CliObservation+BeamMessage.kRemoveBikeMsg, (s) => JsonConvert.DeserializeObject<ApianRemoveBikeObservation>(s) },
            {ApianMessage.CliObservation+BeamMessage.kPlaceClaimMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceClaimObservation>(s) },
            {ApianMessage.CliObservation+BeamMessage.kPlaceHitMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceHitObservation>(s) },
            {ApianMessage.CliObservation+BeamMessage.kPlaceRemovedMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceRemovedObservation>(s) },

            {ApianMessage.CliCommand+BeamMessage.kNewPlayer, (s) => JsonConvert.DeserializeObject<ApianNewPlayerCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kPlayerLeft, (s) => JsonConvert.DeserializeObject<ApianPlayerLeftCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kBikeTurnMsg, (s) => JsonConvert.DeserializeObject<ApianBikeTurnCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kBikeCommandMsg, (s) => JsonConvert.DeserializeObject<ApianBikeCommandCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kBikeCreateData, (s) => JsonConvert.DeserializeObject<ApianBikeCreateCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kRemoveBikeMsg, (s) => JsonConvert.DeserializeObject<ApianRemoveBikeCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kPlaceClaimMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceClaimCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kPlaceHitMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceHitCommand>(s) },
            {ApianMessage.CliCommand+BeamMessage.kPlaceRemovedMsg, (s) => JsonConvert.DeserializeObject<ApianPlaceRemovedCommand>(s) },

            // TODO: &&&& This is AWFUL! I want the checkpoint command to be a proper ApianCommand so it has a sequence # is part of
            // the command stream and all - but the deserialization "chain" that I've create really only works if the command is deserialized
            // here. est plan fo rthe mment is probably a special-case "hook" in FromJSON() below to pass Apian-defined mock-client-commands
            // to Apian to decode.
            {ApianMessage.CliCommand+ApianMessage.CheckpointMsg, (s) => JsonConvert.DeserializeObject<ApianCheckpointCommand>(s) },
        };

        public static ApianMessage FromJSON(string msgType, string json)
        {
            // Deserialize once. May have to do it again
            ApianMessage aMsg = ApianMessageDeserializer.FromJSON(msgType, json);

            string subType = ApianMessageDeserializer.GetSubType(aMsg);

            return  aMsg.MsgType == ApianMessage.GroupMessage ? ApianGroupMessageDeserializer.FromJson(subType, json) :
                subType == null ? aMsg :
                     beamDeserializers[msgType+subType](json) as ApianMessage;
        }

    }


}