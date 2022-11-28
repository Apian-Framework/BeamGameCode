using System.Collections;
using System.Collections.Generic;
using Apian;
using UnityEngine;


namespace BeamGameCode
{
    public class BikeDynState
    {
        public Vector2 position {get;}
        public Heading heading {get;}
        public float speed {get;}
        public int score {get;}
        public TurnDir pendingTurn {get;}

        public BikeDynState(Vector2 _position, Heading _heading, float _speed, int _score, TurnDir _pendingTurn)
        {
            position = _position;
            heading = _heading;
            speed = _speed;
            score = _score;
            pendingTurn = _pendingTurn;
        }
    }

    public interface IBike : IApianCoreData
    {
        string bikeId {get;}
        string playerAddr {get;}
        string name {get;}
        Team team { get;}
        string ctrlType {get;}
        long baseTime {get;}
        Vector2 basePosition {get;}
        Heading baseHeading { get;}
        float speed { get; }
        int score {get;}
        TurnDir basePendingTurn { get;}
        void Loop(long apianTime);
        void AddScore(int val);
        //Vector2 Position(long curMs);
        BikeDynState DynamicState(long curTimeMs);

    }

}
