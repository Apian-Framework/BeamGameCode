using UnityEngine;
using UniLog;
using BeamGameCode;

namespace BikeControl
{
    public interface IBikeControl
    {
        void Setup(IBeamApplication appl, IBeamAppCore core, IBike beBike);
        void Loop(long curTime, int frameMs);
        bool RequestTurn(TurnDir dir, bool allowDeferred = false);
    }

    public abstract class BikeControlBase : IBikeControl
    {
        protected IBeamAppCore appCore;
        IBeamApplication appl;
        protected BaseBike bb;
        protected BikeDynState bbDynState;
        protected TurnDir stashedTurn = TurnDir.kUnset; // if turn is requested too late then save it and apply it after the turn is done

        public UniLogger Logger;

        public BikeControlBase()
        {
            Logger = UniLogger.GetLogger("BikeCtrl");
        }

        public void Setup(IBeamApplication beamApp, IBeamAppCore core, IBike ibike)
        {
            appl = beamApp;
            appCore = core;
            bb = ibike as BaseBike;
            SetupImpl();
        }

        public abstract void SetupImpl(); // do any implmentation-specific setup

        public virtual void Loop(long curTime, int frameMs)
        {
            bbDynState = bb.DynamicState(curTime);
            if (stashedTurn != TurnDir.kUnset)
            {
                if (!bb.CloseToGridPoint(bbDynState.position))
                {
                    // Turn is requested, and we are not close to a point
                    Logger.Verbose($"{this.GetType().Name} Bike {bb.name} Executing turn.");
                    appl.beamGameNet.SendBikeTurnReq(appCore.ApianGroupId, bb, curTime, stashedTurn, bb.UpcomingGridPoint(bbDynState.position));
                    stashedTurn = TurnDir.kUnset;
                }
            }
        }

        public virtual bool RequestTurn(TurnDir dir, bool allowDeferred = false)
        {
            // If we are too close to the upcoming point to be able to turn then assign it to the next point,
            // otherwise send out a request.
            // Current limit is 1 bike length
            bool posted = false;
            if (bb.CloseToGridPoint(bb.DynamicState(appCore.CurrentRunningGameTime).position)) // too close to a grid point to turn
            {
                if (allowDeferred)
                {
                    Logger.Verbose($"{this.GetType().Name} Bike {bb.name} requesting deferred turn.");
                    stashedTurn = dir;
                }
            }
            else
            {
                // cancel anything stashed (can this happen?)
                stashedTurn = TurnDir.kUnset;

                if ((dir == bb.basePendingTurn) ||  (dir == TurnDir.kStraight && bb.basePendingTurn == TurnDir.kUnset))
                    Logger.Verbose($"RequestTurn() ignoring do-nothing {dir}");
                else
                {
                    appl.beamGameNet.SendBikeTurnReq(appCore.ApianGroupId, bb, appCore.CurrentRunningGameTime, dir, bb.UpcomingGridPoint(bbDynState.position));
                    posted = true;
                }
            }
            return posted;
        }

    }
}