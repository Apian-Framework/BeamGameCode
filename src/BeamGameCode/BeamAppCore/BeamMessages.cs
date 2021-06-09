using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using Apian;

namespace BeamGameCode
{
    public class BeamMessage : ApianCoreMessage
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
                xPos = ib.basePosition.x;
                yPos = ib.basePosition.y;
                heading = ib.baseHeading;
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

        // No conflict detection
    }

    public class PlayerLeftMsg : BeamMessage
    {
        public string peerId;
        public PlayerLeftMsg(long ts, string _peerId) : base(kPlayerLeft, ts) => peerId = _peerId;
        public PlayerLeftMsg() : base() {}

        // No conflict detection
    }

    public class BikeCreateMsg : BeamMessage
    {
        public string bikeId;
        public string peerId;
        public string name;
        public Team team;
        public int score;
        public string ctrlType;
        public long timeAtPos;
        public float xPos;
        public float yPos;
        public Heading heading;

        public BikeCreateMsg(long ts, IBike ib) : base(kBikeCreateData, ts)
        {
            bikeId = ib.bikeId;
            peerId = ib.peerId;
            name = ib.name;
            team = ib.team;
            score = ib.score;
            ctrlType = ib.ctrlType;
            timeAtPos = ib.baseTime;
            xPos = ib.basePosition.x;
            yPos = ib.basePosition.y;
            heading = ib.baseHeading;
        }

        public BikeCreateMsg() : base() {}

        public BaseBike ToBike(BeamCoreState gd)
        {
            return new BaseBike(gd, bikeId, peerId , name, team, ctrlType, timeAtPos, new Vector2(xPos, yPos), heading);
        }
    }

    public class RemoveBikeMsg : BeamMessage
    {
        public string bikeId;
        public RemoveBikeMsg() : base() {}
        public RemoveBikeMsg(long ts, string _bikeId) : base(kRemoveBikeMsg, ts) { bikeId = _bikeId; }

        // No conflict detection
    }

    public class BikeTurnMsg : BeamMessage
    {
        // TODO: use place hashes instad of positions?
        public string bikeId;
        public string ownerPeer; // helpful in case bike is gone on arrival
        public TurnDir dir;
        public Heading entryHead; // From here down is for validation
        public float nextPtX;
        public float nextPtZ;
        public BikeState bikeState; // TODO: we shouldn't need ALL of this
        public BikeTurnMsg() : base()  {}

        public BikeTurnMsg(long ts, IBike ib, TurnDir _dir, Vector2 nextGridPt) : base(kBikeTurnMsg, ts)
        {
            bikeId = ib.bikeId;
            ownerPeer = ib.peerId;
            dir = _dir;
            entryHead = ib.baseHeading;
            nextPtX = nextGridPt.x;
            nextPtZ = nextGridPt.y;
            bikeState = new BikeState(ib);
        }
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

            // isValidAfterFuncs = new Dictionary<string, Func<ApianCoreMessage,(ApianCoreMessage.ValidState, string)>>()
            // {
            //     { kPlaceHitMsg, ValidAfterPlaceHit},
            //     { kRemoveBikeMsg, ValidAfterRemoveBike},
            //     { kPlaceRemovedMsg, ValidAfterPlaceRemoved}
            // };
        }
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

       // No observation conflict detection
    }

    static public class BeamCoreMessageDeserializer
    {

         public static Dictionary<string, Func<string, ApianCoreMessage>> beamDeserializers = new  Dictionary<string, Func<string, ApianCoreMessage>>()
         {
            {BeamMessage.kNewPlayer, (s) => JsonConvert.DeserializeObject<NewPlayerMsg>(s) },
            {BeamMessage.kPlayerLeft, (s) => JsonConvert.DeserializeObject<PlayerLeftMsg>(s) },
            {BeamMessage.kBikeTurnMsg, (s) => JsonConvert.DeserializeObject<BikeTurnMsg>(s) },
            {BeamMessage.kBikeCommandMsg, (s) => JsonConvert.DeserializeObject<BikeCommandMsg>(s) },
            {BeamMessage.kBikeCreateData, (s) => JsonConvert.DeserializeObject<BikeCreateMsg>(s) },
            {BeamMessage.kRemoveBikeMsg, (s) => JsonConvert.DeserializeObject<RemoveBikeMsg>(s) },
            {BeamMessage.kPlaceClaimMsg, (s) => JsonConvert.DeserializeObject<PlaceClaimMsg>(s) },
            {BeamMessage.kPlaceHitMsg, (s) => JsonConvert.DeserializeObject<PlaceHitMsg>(s) },
            {BeamMessage.kPlaceRemovedMsg, (s) => JsonConvert.DeserializeObject<PlaceRemovedMsg>(s) },

            // TODO: &&&& This is AWFUL! I want the checkpoint command to be a proper ApianCommand so it has a sequence # is part of
            // the command stream and all - but the deserialization "chain" that I've create really only works if the command is deserialized
            // here. I could check for the msgType in this dict and if not there get it from an ApianMessage-defined one, but it seems a shame
            // to do the test?
            // TODO: Nah - do the test
            {ApianMessage.CheckpointMsg, (s) => JsonConvert.DeserializeObject<ApianCheckpointMsg>(s) },
         };

        public static ApianCoreMessage FromJSON(string coreMsgType, string json)
        {
            return  beamDeserializers[coreMsgType](json) as ApianCoreMessage;
        }
    }


}