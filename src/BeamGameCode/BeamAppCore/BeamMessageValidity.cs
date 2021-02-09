using System;
using System.Collections.Generic;
using Apian;

namespace BeamGameCode
{
    public class BeamMessageValidity
    {
        // - PlaceClaimMsg
        //     invalidated after?
        //       - PlaceClaimMsg
        //       - PlaceHitMsg
        //       - RemoveBikeMsg
        //     validated after?
        //       - PlaceRemovedMsg
        // - PlaceHitMsg
        //     invalidated after?
        //       - PlaceRemovedMsg
        //       - RemoveBikeMsg
        //     validated after?
        //       - PlaceClaimMsg

        public static Dictionary<string,Func<BeamMessage,BeamMessage,(ApianConflictResult, string)>> ObsConflictFuncs
            = new Dictionary<string,Func<BeamMessage,BeamMessage,(ApianConflictResult, string)>>()
        {
            {BeamMessage.kPlaceClaimMsg+BeamMessage.kPlaceClaimMsg, ClaimAfterClaim },
            {BeamMessage.kPlaceHitMsg+BeamMessage.kPlaceClaimMsg, ClaimAfterHit },
            {BeamMessage.kRemoveBikeMsg+BeamMessage.kPlaceClaimMsg, ClaimAfterRemoveBike },
            {BeamMessage.kPlaceRemovedMsg+BeamMessage.kPlaceClaimMsg, ClaimAfterPlaceRemoved },
            {BeamMessage.kPlaceRemovedMsg+BeamMessage.kPlaceHitMsg, HitAfterPlaceRemoved },
            {BeamMessage.kRemoveBikeMsg+BeamMessage.kPlaceHitMsg, HitAfterRemoveBike },
            {BeamMessage.kPlaceClaimMsg+BeamMessage.kPlaceHitMsg, HitAfterPlaceClaim }
        };

        public static (ApianConflictResult result, string reason) ValidateObservations(BeamMessage prevMsg, BeamMessage testMsg)
        {
            string key = prevMsg.MsgType + testMsg.MsgType;
            return   !ObsConflictFuncs.ContainsKey(key)
                ? (ApianConflictResult.Unaffected, null)
                : ObsConflictFuncs[key](prevMsg, testMsg) ;

        }

        public static (ApianConflictResult, string) ClaimAfterClaim(BeamMessage amsg, BeamMessage bmsg)
        {
            PlaceClaimMsg msg = amsg as PlaceClaimMsg;
            PlaceClaimMsg msg2 = bmsg as PlaceClaimMsg;
            if (BeamPlace.MakePosHash(msg.xIdx,msg.zIdx) == BeamPlace.MakePosHash(msg2.xIdx,msg2.zIdx))
                return (ApianConflictResult.Invalidated, "Place already claimed");
            return (ApianConflictResult.Unaffected, null);
        }

        public static (ApianConflictResult, string) ClaimAfterHit(BeamMessage amsg, BeamMessage bmsg)
        {
            PlaceHitMsg msg = amsg as PlaceHitMsg;
            PlaceClaimMsg msg2 = bmsg as PlaceClaimMsg;
            if (BeamPlace.MakePosHash(msg.xIdx,msg.zIdx) == BeamPlace.MakePosHash(msg2.xIdx,msg2.zIdx))
                return (ApianConflictResult.Invalidated, "Place hit");
            return (ApianConflictResult.Unaffected, null);
        }

        public static (ApianConflictResult, string) ClaimAfterRemoveBike(BeamMessage amsg, BeamMessage bmsg)
        {
            RemoveBikeMsg msg = amsg as RemoveBikeMsg;
            PlaceClaimMsg msg2 = bmsg as PlaceClaimMsg;
            if (msg.bikeId == msg2.bikeId)
                return (ApianConflictResult.Invalidated, "Bike removed");
            return (ApianConflictResult.Unaffected, null);
        }

        public static (ApianConflictResult, string) ClaimAfterPlaceRemoved(BeamMessage amsg, BeamMessage bmsg)
        {
            PlaceRemovedMsg msg = amsg as PlaceRemovedMsg;
            PlaceClaimMsg msg2 = bmsg as PlaceClaimMsg;
            if (BeamPlace.MakePosHash(msg.xIdx,msg.zIdx) == BeamPlace.MakePosHash(msg2.xIdx,msg2.zIdx))
                return (ApianConflictResult.Validated, "Place removed");
            return (ApianConflictResult.Unaffected, null);
        }

        public static (ApianConflictResult, string) HitAfterPlaceRemoved(BeamMessage amsg, BeamMessage bmsg)
        {
            PlaceRemovedMsg msg = amsg as PlaceRemovedMsg;
            PlaceHitMsg msg2 = bmsg as PlaceHitMsg;
            if (BeamPlace.MakePosHash(msg.xIdx,msg.zIdx) == BeamPlace.MakePosHash(msg2.xIdx,msg2.zIdx))
                return (ApianConflictResult.Invalidated, "Place removed");
            return (ApianConflictResult.Unaffected, null);
        }

        public static (ApianConflictResult, string) HitAfterRemoveBike(BeamMessage amsg, BeamMessage bmsg)
        {
            RemoveBikeMsg msg = amsg as RemoveBikeMsg;
            PlaceHitMsg msg2 = bmsg as PlaceHitMsg;
            if (msg.bikeId == msg2.bikeId)
                return (ApianConflictResult.Invalidated, "Bike removed");
            return (ApianConflictResult.Unaffected, null);
        }

        public static (ApianConflictResult, string) HitAfterPlaceClaim(BeamMessage amsg, BeamMessage bmsg)
        {
            PlaceClaimMsg msg = amsg as PlaceClaimMsg;
            PlaceHitMsg msg2 = bmsg as PlaceHitMsg;
            if (BeamPlace.MakePosHash(msg.xIdx,msg.zIdx) == BeamPlace.MakePosHash(msg2.xIdx,msg2.zIdx))
                return (ApianConflictResult.Validated, "Place claimed");
            return (ApianConflictResult.Unaffected, null);
        }





    }
}