using UnityEngine;

namespace BeamBackend
{
    public class BeamMessage
    {
        public const string kGameCreated = "100";
        public const string kGameJoined = "101";
        public const string kPeerJoined = "102";
        public const string kPeerLeft = "103";      
        public const string kBikeCreateData = "104";
        public const string kBikeDataReq = "105";
        public const string kBikeUpdate = "106";
         
        public string msgType;
        public BeamMessage(string t) => msgType = t;
    }

    //
    // Basic connection messages
    //

    public class GameCreatedMsg : BeamMessage
    {
        public string gameId;
        public GameCreatedMsg(string _gameId) : base(kGameCreated) => gameId = _gameId;
    }
    
    public class GameJoinedMsg : BeamMessage
    {
        public string gameId;
        public string localId;
        public GameJoinedMsg(string _gameId, string _localId) :  base(kGameJoined)
        {
            gameId = _gameId; 
            localId = _localId;
        }
    }

    public class PeerJoinedMsg : BeamMessage
    {
        public BeamPeer peer;
        public PeerJoinedMsg(BeamPeer _p) : base(kPeerJoined) => peer = _p;
    }    

    public class PeerLeftMsg : BeamMessage
    {
        public BeamPeer peer;
        public PeerLeftMsg(BeamPeer _p) : base(kPeerLeft) => peer = _p;
    }      

    //
    // GameNet messages
    //
    //
    public class BikeCreateDataMsg : BeamMessage
    {
        public string bikeId; 
        public string peerId;
        public string name;
        public Team team;
        public int score;     
        public int ctrlType;
        public float xPos;
        public float yPos;
        public Heading heading;     
        public float speed;

        public BikeCreateDataMsg(IBike ib) : base(kBikeCreateData) 
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
            speed = ib.speed;
        }

        public BikeCreateDataMsg() : base(kBikeCreateData) 
        {
        }        

        public IBike ToBike(BeamGameInstance gi)
        {
            return new BaseBike(gi, bikeId,peerId, name, team, ctrlType, new Vector2(xPos, yPos), heading);
        }
    }

    public class BikeDataReqMsg : BeamMessage
    {
        public string bikeId;
        public BikeDataReqMsg(string _id) : base(kBikeDataReq) => bikeId = _id;        
    }

    public class BikeUpdateMsg : BeamMessage
    {
        public string bikeId; 
        public int score;     
        public float xPos;
        public float yPos;
        public Heading heading;   
        public float speed;  

        public BikeUpdateMsg(IBike ib) : base(kBikeUpdate) 
        {
            bikeId = ib.bikeId;
            score = ib.score;
            xPos = ib.position.x;
            yPos = ib.position.y;
            heading = ib.heading;
            speed = ib.speed;
        }
    }

}