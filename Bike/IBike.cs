﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace BeamBackend
{
    public interface IBike 
    {
        string bikeId {get;}  
        string peerId {get;}
        string name {get;}
        Team team { get;}   
        int ctrlType {get;}        
        Vector2 position {get;}   
        Heading heading { get;} 
        float speed { get; }
        int score {get;}          
        void Loop(float secs);  

        // Temporary ctrl stuff
        TurnDir pendingTurn { get;}
        // Other ctrl stuff
        void Go();
        void Stop();
    }

}
